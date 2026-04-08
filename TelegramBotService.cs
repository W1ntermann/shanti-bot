using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using ShantiBotDi.Db;
using ShantiBotDi.Models;
using ShantiBotDi.Services;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Polling;
using Telegram.Bot.Types.ReplyMarkups;
using static ShantiBotDi.Helpers.InvestmentHelper;
using User = ShantiBotDi.Models.User;

namespace ShantiBotDi;

public class TelegramBotService : IHostedService
{
    private readonly ITelegramBotClient _botClient;
    private readonly SettingsService _settingsService;
    private readonly IRegisterService _registerService;
    private readonly BotDbContext _botDbContext;
    private readonly IManageService _manageService;
    private readonly IProfileService _profileService;
    private readonly IOperationService _operationService;
    private readonly CancellationTokenSource _cts = new();
    private readonly IUserStateService _userStateService;
    private readonly WithdrawalAdminService _withdrawalAdminService;
    private readonly Dictionary<long, InvestmentDuration> _userDurations = new();
    private readonly Dictionary<long, decimal> _pendingDeposits = new();
    private string? _botUsername;
    private readonly IInvestmentAdminService _investmentAdminService;
    private readonly ILoggerService _logger;
    private readonly IReferralService _referralService;
    private DateTime _lastInvestmentCheck = DateTime.MinValue;
    private readonly IQuestService _questService;
    private readonly IMaintenanceService _maintenanceService;
    private readonly ITradingSimulationService _tradingSimulationService;
    private readonly IChartRendererService _chartRendererService;
    private readonly IDelayCompensationService _delayCompensationService;
    private readonly ILocalizationService _localizationService;


    private static readonly Dictionary<string, string> FixedWalletAddresses = new()

    {
        { "Primary Wallet", "TVRyj7JpLoWPLbZ9rb6dMzFLf2Pnm24kkj" },
    };

    private readonly Dictionary<long, string> _pendingUsernames = new();
    private readonly Dictionary<long, string> _pendingPasswords = new();
    private readonly Dictionary<long, bool> _waitingInvestmentUserId = new();
    private readonly Dictionary<long, int> _pendingBonusMessages = new();

    public TelegramBotService(
        ITelegramBotClient botClient,
        IManageService manageService,
        IProfileService profileService,
        IOperationService operationService,
        IUserStateService userStateService,
        BotDbContext botDbContext,
        WithdrawalAdminService withdrawalAdminService,
        IRegisterService registerService,
        SettingsService settingsService,
        IInvestmentAdminService investmentAdminService,
        ILoggerService logger,
        IReferralService referralService,
        IQuestService questService,
        IMaintenanceService maintenanceService,
        ITradingSimulationService tradingSimulationService,
        IChartRendererService chartRendererService,
        IDelayCompensationService delayCompensationService,
        ILocalizationService localizationService)
    {
        _botClient = botClient;
        _manageService = manageService;
        _profileService = profileService;
        _operationService = operationService;
        _userStateService = userStateService;
        _botDbContext = botDbContext;
        _withdrawalAdminService = withdrawalAdminService;
        _registerService = registerService;
        _settingsService = settingsService;
        _investmentAdminService = investmentAdminService;
        _logger = logger;
        _referralService = referralService;
        _questService = questService;
        _maintenanceService = maintenanceService;
        _tradingSimulationService = tradingSimulationService;
        _chartRendererService = chartRendererService;
        _delayCompensationService = delayCompensationService;
        _localizationService = localizationService;
    }

    private async Task ShowMainMenuForAllUsersAsync()
    {
        try
        {
            // Отримуємо всіх користувачів з бази (без відстеження, лише потрібні поля)
            var users = await _botDbContext.Users
                .AsNoTracking()
                .Select(u => new { u.TelegramId })
                .ToListAsync();

            foreach (var user in users)
            {
                try
                {
                    await ShowMainMenuAsync(user.TelegramId);
                    await Task.Delay(100); // Затримка між повідомленнями
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error showing menu for user {user.TelegramId}: {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error getting users from database: {ex.Message}");
        }
    }

    private async Task<bool> TryHandleLanguageSelectionCallbackAsync(
        ITelegramBotClient bot,
        CallbackQuery callback,
        long chatId,
        string? callbackData)
    {
        if (string.IsNullOrWhiteSpace(callbackData))
        {
            return false;
        }

        const string onboardingPrefix = "lang_";
        const string settingsPrefix = "set_language_";
        string? callbackSuffix = null;
        var fromSettings = false;

        if (callbackData.StartsWith(onboardingPrefix, StringComparison.OrdinalIgnoreCase))
        {
            callbackSuffix = callbackData[onboardingPrefix.Length..];
        }
        else if (callbackData.StartsWith(settingsPrefix, StringComparison.OrdinalIgnoreCase))
        {
            callbackSuffix = callbackData[settingsPrefix.Length..];
            fromSettings = true;
        }

        if (callbackSuffix == null || !_localizationService.TryGetLanguageByCallback(callbackSuffix, out var language))
        {
            return false;
        }

        if (fromSettings)
        {
            await _settingsService.UpdateUserLanguageAsync(chatId, language.Code);
            await bot.AnswerCallbackQuery(callback.Id, _localizationService.GetText(language.Code, "language.saved"));
            await ShowMainMenuAsync(chatId);
            return true;
        }

        await _manageService.UpdateUserLanguageAsync(chatId, language.Code);
        await bot.AnswerCallbackQuery(callback.Id, _localizationService.GetText(language.Code, "language.saved"));
        await ShowAuthOptions(chatId, language.Code);
        return true;
    }

    private InlineKeyboardButton[][] BuildLanguageSelectionButtons(string callbackPrefix)
    {
        var rows = new List<InlineKeyboardButton[]>();
        var currentRow = new List<InlineKeyboardButton>(2);

        foreach (var language in _localizationService.GetSupportedLanguages())
        {
            currentRow.Add(
                InlineKeyboardButton.WithCallbackData(
                    $"{language.FlagEmoji} {language.NativeName}",
                    $"{callbackPrefix}{language.CallbackSuffix}"));

            if (currentRow.Count == 2)
            {
                rows.Add(currentRow.ToArray());
                currentRow.Clear();
            }
        }

        if (currentRow.Count > 0)
        {
            rows.Add(currentRow.ToArray());
        }

        return rows.ToArray();
    }

    private string GetUserLanguageCode(User? user)
    {
        return user?.PreferredLanguage ?? user?.Language ?? BotLanguageCodes.English;
    }

    private string T(
        string? language,
        string english,
        string hinglish,
        string russian,
        string farsi,
        string arabic,
        string chinese)
    {
        var normalized = _localizationService.NormalizeCode(language);
        var text = normalized switch
        {
            BotLanguageCodes.Hinglish => hinglish,
            BotLanguageCodes.Russian => russian,
            BotLanguageCodes.Farsi => farsi,
            BotLanguageCodes.Arabic => arabic,
            BotLanguageCodes.SimplifiedChinese => chinese,
            _ => english
        };

        return _localizationService.FormatText(normalized, text);
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var me = await _botClient.GetMe(cancellationToken);
        _botUsername = me.Username ?? me.Id.ToString();

        var receiverOptions = new ReceiverOptions
        {
            AllowedUpdates = new[] { UpdateType.Message, UpdateType.CallbackQuery }
        };

        _botClient.StartReceiving(
            HandleUpdateAsync,
            HandleErrorAsync,
            receiverOptions,
            cancellationToken: _cts.Token
        );

        Console.WriteLine($"🤖 Bot @{_botUsername} service started...");
    }


    public Task StopAsync(CancellationToken cancellationToken)
    {
        _cts.Cancel();
        Console.WriteLine("🛑 Bot service stopped...");
        return Task.CompletedTask;
    }

    private async Task HandleUpdateAsync(ITelegramBotClient bot, Update update, CancellationToken cancellationToken)
    {
        await CheckInvestmentsPeriodically();

        // ======= 1. Обробка CallbackQuery =======

        if (update.Type == UpdateType.CallbackQuery)
        {
            var callback = update.CallbackQuery!;
            var callbackChatId = callback.Message?.Chat.Id ?? callback.From.Id; // ✅ без NullReference
            var data = callback.Data;
            var callbackMessageId = callback.Message.MessageId;


            Console.WriteLine($"Callback data: {data}");

            // Handle dynamic quest claim callbacks

            if (await TryHandleLanguageSelectionCallbackAsync(bot, callback, callbackChatId, data))
            {
                return;
            }


            switch (data)
            {
                case "start_auth":
                    var userLang = await _manageService.GetUserLanguageAsync(callbackChatId);
                    var authButtons = new InlineKeyboardMarkup(new[]
                    {
                        new[]
                        {
                            InlineKeyboardButton.WithCallbackData(
                                _localizationService.GetText(userLang, "auth.register"),
                                "register"),
                            InlineKeyboardButton.WithCallbackData(
                                _localizationService.GetText(userLang, "auth.login"),
                                "login")
                        }
                    });
                    await bot.SendMessage(callbackChatId,
                        _localizationService.GetText(userLang, "auth.chooseAction"),
                        replyMarkup: authButtons);
                    break;

                case "register":
                    await ShowRegistrationRulesAsync(callbackChatId,
                        await _manageService.GetUserLanguageAsync(callbackChatId));
                    break;

                case "login":
                    await _userStateService.SetStateAsync(callbackChatId, UserAction.WaitingLoginPassword);
                    await bot.SendMessage(callbackChatId,
                        _localizationService.GetText(
                            await _manageService.GetUserLanguageAsync(callbackChatId),
                            "auth.passwordPrompt"));
                    break;

                case "login_confirmed":
                    await bot.AnswerCallbackQuery(callback.Id, "Accepted ✅");
                    await ShowMainMenuAsync(callbackChatId);
                    break;
                case "rules_accepted":
                {
                    var currentLang = await _manageService.GetUserLanguageAsync(callbackChatId);

                    // Встановлюємо стан на очікування username
                    await _userStateService.SetStateAsync(callbackChatId, UserAction.WaitingRegisterUsername);

                    // Надсилаємо підказку для username одразу після натискання
                    await _botClient.SendMessage(callbackChatId,
                        _localizationService.GetText(currentLang, "registration.enterUsername"));
                    break;
                }
                case "admin_investments":
                {
                    if (_investmentAdminService == null)
                    {
                        await _botClient.SendMessage(callbackChatId, "❌ Investment service not available.");
                        break;
                    }

                    await _investmentAdminService.ShowAllUsersSummaryAsync(callbackChatId);
                    var adminKeyboard = new InlineKeyboardMarkup(new[]
                    {
                        new[]
                        {
                            InlineKeyboardButton.WithCallbackData("🔙 Back to Menu", "back_to_menu")
                        }
                    });
                    await bot.SendMessage(callbackChatId, "Your details above", replyMarkup: adminKeyboard);
                    break;
                }

                // === Перегляд інвестицій конкретного користувача ===
                case "admin_investments_user":
                {
                    _waitingInvestmentUserId[callbackChatId] = true;

                    await _botClient.SendMessage(
                        callbackChatId,
                        "Please send the user's TelegramId (digits only)."
                    );
                    break;
                }
                case "read_instructions":
                    // Видаляємо inline кнопку
                    await bot.EditMessageReplyMarkup(callbackChatId, callbackMessageId, null);

                    // Додаємо маленьке підтвердження (опціонально)
                    var readUser = await _manageService.GetUserByTelegramIdAsync(callbackChatId);
                    var readUserLang = GetUserLanguageCode(readUser);
                    string confirmation = T(
                        readUserLang,
                        "👍 Let's get started!",
                        "👍 Chalo shuru karein!",
                        "👍 Давайте начнем!",
                        "👍 بیایید شروع کنیم!",
                        "👍 لنبدأ الآن!",
                        "👍 让我们开始吧！");

                    await bot.SendMessage(callbackChatId, confirmation);

                    // Показуємо головне меню
                    await ShowMainMenuAsync(callbackChatId);

                    // Підтверджуємо натискання кнопки
                    await bot.AnswerCallbackQuery(callback.Id);
                    break;
                case "about":
                    var aboutUser = await _manageService.GetUserByTelegramIdAsync(callbackChatId);
                                        var aboutLang = GetUserLanguageCode(aboutUser);

                                        var aboutText = T(
                                                aboutLang,
                                                "🌟 *STARQUANTUM.AI TRADING PLATFORM* 🌟\n\n🤖 *Advanced AI Trading Technology*\nStarQuantum.AI is a sophisticated algorithmic trading system designed for consistent and secure capital growth.\n\n🚀 *How Our System Works*\n\n💳 *1. Deposit Funds*\n• Transfer USDT to our secure Trust Wallet\n• Minimum investment: 1 USDT\n• TRC20 network only\n\n📊 *2. AI Trading Execution*\n• Advanced algorithms analyze multiple markets\n• Diversified investment strategies\n• Real-time market monitoring\n\n📈 *3. Automated Growth*\n• Fully automated trading process\n• Daily profit accumulation\n• Transparent performance tracking\n\n🛡️ *Security Features*\n\n🔐 *Proprietary Technology*\n• Unique AI algorithm cannot be replicated\n• Advanced risk management systems\n\n💎 *Funds Protection*\n• Secure from wallet freezes\n• No third-party control\n• No KYC requirements\n\n⚡ *256-bit Encryption*\n• Military-grade security protocols\n• Regular security audits\n\n🎯 *Our Investment Philosophy*\n\n📊 *Steady Growth Focus*\n• Consistent returns over time\n• Minimal risk exposure\n• No risky pump trades\n\n🌱 *Long-Term Strategy*\n• Sustainable profit generation\n• Diversified portfolio approach\n• Continuous algorithm optimization",
                                                "🌟 *STARQUANTUM.AI TRADING PLATFORM* 🌟\n\n🤖 *Advanced AI Trading Technology*\nStarQuantum.AI ek advanced algorithmic trading system hai jo consistent aur secure capital growth ke liye design kiya gaya hai.\n\n🚀 *Hamara System Kaise Kaam Karta Hai*\n\n💳 *1. Funds Deposit Karein*\n• Hamare secure Trust Wallet mein USDT transfer karein\n• Minimum investment: 1 USDT\n• Sirf TRC20 network\n\n📊 *2. AI Trading Execution*\n• Advanced algorithms multiple markets ka analysis karte hain\n• Diversified investment strategies\n• Real-time market monitoring\n\n📈 *3. Automated Growth*\n• Fully automated trading process\n• Daily profit accumulation\n• Transparent performance tracking\n\n🛡️ *Security Features*\n\n🔐 *Proprietary Technology*\n• Unique AI algorithm copy nahi ho sakta\n• Advanced risk management systems\n\n💎 *Funds Protection*\n• Wallet freezes se secure\n• Third-party control nahi\n• KYC requirements nahi\n\n⚡ *256-bit Encryption*\n• Military-grade security protocols\n• Regular security audits\n\n🎯 *Hamari Investment Philosophy*\n\n📊 *Steady Growth Focus*\n• Consistent returns over time\n• Minimal risk exposure\n• Risky pump trades nahi\n\n🌱 *Long-Term Strategy*\n• Sustainable profit generation\n• Diversified portfolio approach\n• Continuous algorithm optimization",
                                                "🌟 *ПЛАТФОРМА STARQUANTUM.AI TRADING* 🌟\n\n🤖 *Передовые AI-технологии в трейдинге*\nStarQuantum.AI - это продвинутая алгоритмическая торговая система, созданная для стабильного и безопасного роста капитала.\n\n🚀 *Как работает наша система*\n\n💳 *1. Пополнение средств*\n• Переведите USDT на наш защищенный Trust Wallet\n• Минимальная инвестиция: 1 USDT\n• Только сеть TRC20\n\n📊 *2. AI-исполнение сделок*\n• Продвинутые алгоритмы анализируют несколько рынков\n• Диверсифицированные инвестиционные стратегии\n• Мониторинг рынка в реальном времени\n\n📈 *3. Автоматический рост*\n• Полностью автоматизированный торговый процесс\n• Ежедневное накопление прибыли\n• Прозрачное отслеживание результатов\n\n🛡️ *Функции безопасности*\n\n🔐 *Собственная технология*\n• Уникальный AI-алгоритм невозможно скопировать\n• Продвинутые системы управления рисками\n\n💎 *Защита средств*\n• Защита от блокировки кошельков\n• Отсутствие контроля третьих лиц\n• Нет требований KYC\n\n⚡ *256-битное шифрование*\n• Военный уровень протоколов безопасности\n• Регулярные аудиты безопасности\n\n🎯 *Наша инвестиционная философия*\n\n📊 *Фокус на стабильном росте*\n• Последовательная доходность во времени\n• Минимальный уровень риска\n• Без рискованных памп-сделок\n\n🌱 *Долгосрочная стратегия*\n• Устойчивая генерация прибыли\n• Диверсифицированный портфель\n• Постоянная оптимизация алгоритма",
                                                "🌟 *پلتفرم معاملاتی STARQUANTUM.AI* 🌟\n\n🤖 *فناوری پیشرفته معاملات مبتنی بر هوش مصنوعی*\nStarQuantum.AI یک سیستم پیشرفته معاملاتی الگوریتمی است که برای رشد پایدار و امن سرمایه طراحی شده است.\n\n🚀 *سیستم ما چگونه کار می کند*\n\n💳 *1. واریز وجه*\n• USDT را به کیف پول امن Trust Wallet ما منتقل کنید\n• حداقل سرمایه گذاری: 1 USDT\n• فقط شبکه TRC20\n\n📊 *2. اجرای معاملات با هوش مصنوعی*\n• الگوریتم های پیشرفته چندین بازار را تحلیل می کنند\n• استراتژی های سرمایه گذاری متنوع\n• پایش لحظه ای بازار\n\n📈 *3. رشد خودکار*\n• فرآیند معامله کاملاً خودکار\n• انباشت سود روزانه\n• رهگیری شفاف عملکرد\n\n🛡️ *ویژگی های امنیتی*\n\n🔐 *فناوری اختصاصی*\n• الگوریتم منحصربه فرد AI قابل کپی نیست\n• سیستم های پیشرفته مدیریت ریسک\n\n💎 *محافظت از دارایی*\n• ایمن در برابر مسدود شدن کیف پول\n• بدون کنترل شخص ثالث\n• بدون نیاز به KYC\n\n⚡ *رمزگذاری 256 بیتی*\n• پروتکل های امنیتی در سطح نظامی\n• ممیزی های امنیتی منظم\n\n🎯 *فلسفه سرمایه گذاری ما*\n\n📊 *تمرکز بر رشد پایدار*\n• بازدهی منظم در طول زمان\n• حداقل قرارگیری در معرض ریسک\n• بدون معاملات پامپ پرخطر\n\n🌱 *استراتژی بلندمدت*\n• تولید سود پایدار\n• رویکرد پرتفوی متنوع\n• بهینه سازی مستمر الگوریتم",
                                                "🌟 *منصة STARQUANTUM.AI TRADING* 🌟\n\n🤖 *تقنية تداول متقدمة بالذكاء الاصطناعي*\nStarQuantum.AI هو نظام تداول خوارزمي متطور مصمم لتحقيق نمو ثابت وآمن لرأس المال.\n\n🚀 *كيف يعمل نظامنا*\n\n💳 *1. إيداع الأموال*\n• قم بتحويل USDT إلى محفظة Trust Wallet الآمنة الخاصة بنا\n• الحد الأدنى للاستثمار: 1 USDT\n• شبكة TRC20 فقط\n\n📊 *2. تنفيذ التداول بالذكاء الاصطناعي*\n• تقوم خوارزميات متقدمة بتحليل عدة أسواق\n• استراتيجيات استثمار متنوعة\n• مراقبة السوق في الوقت الفعلي\n\n📈 *3. النمو الآلي*\n• عملية تداول مؤتمتة بالكامل\n• تراكم أرباح يومي\n• تتبع أداء شفاف\n\n🛡️ *ميزات الأمان*\n\n🔐 *تقنية خاصة*\n• خوارزمية AI فريدة لا يمكن نسخها\n• أنظمة متقدمة لإدارة المخاطر\n\n💎 *حماية الأموال*\n• محمي من تجميد المحافظ\n• بدون تحكم من طرف ثالث\n• لا توجد متطلبات KYC\n\n⚡ *تشفير 256 بت*\n• بروتوكولات أمان بمستوى عسكري\n• تدقيقات أمنية منتظمة\n\n🎯 *فلسفتنا الاستثمارية*\n\n📊 *التركيز على النمو المستقر*\n• عوائد متسقة مع الوقت\n• حد أدنى من التعرض للمخاطر\n• بدون صفقات ضخ عالية المخاطر\n\n🌱 *استراتيجية طويلة المدى*\n• توليد أرباح مستدامة\n• نهج محفظة متنوعة\n• تحسين مستمر للخوارزمية",
                                                "🌟 *STARQUANTUM.AI TRADING 平台* 🌟\n\n🤖 *先进的 AI 交易技术*\nStarQuantum.AI 是一套先进的算法交易系统，旨在实现稳定且安全的资本增长。\n\n🚀 *我们的系统如何运作*\n\n💳 *1. 充值资金*\n• 将 USDT 转入我们的安全 Trust Wallet\n• 最低投资额：1 USDT\n• 仅支持 TRC20 网络\n\n📊 *2. AI 交易执行*\n• 先进算法分析多个市场\n• 多元化投资策略\n• 实时市场监控\n\n📈 *3. 自动增长*\n• 全自动交易流程\n• 每日利润累积\n• 透明的业绩追踪\n\n🛡️ *安全特性*\n\n🔐 *专有技术*\n• 独特的 AI 算法无法被复制\n• 先进的风险管理系统\n\n💎 *资金保护*\n• 防止钱包冻结影响\n• 无第三方控制\n• 无需 KYC\n\n⚡ *256 位加密*\n• 军工级安全协议\n• 定期安全审计\n\n🎯 *我们的投资理念*\n\n📊 *稳健增长导向*\n• 长期稳定回报\n• 最小化风险暴露\n• 不进行高风险拉盘交易\n\n🌱 *长期策略*\n• 可持续利润生成\n• 多元化投资组合\n• 持续优化算法");

                    var aboutKeyboard = new InlineKeyboardMarkup(new[]
                    {
                        new[]
                        {
                            InlineKeyboardButton.WithCallbackData(
                                T(aboutLang, "🔙 Back to Menu", "🔙 Wapas Menu", "🔙 Назад в меню", "🔙 بازگشت به منو", "🔙 العودة إلى القائمة", "🔙 返回菜单"),
                                "back_to_menu")
                        }
                    });

                    await EditOrSendAsync(
                        callbackChatId,
                        callbackMessageId,
                        aboutText,
                        parseMode: ParseMode.Markdown,
                        replyMarkup: aboutKeyboard
                    );
                    break;
                case "back_to_menu":
                    try
                    {
                        Console.WriteLine($"Back to menu pressed by {callback.From.Id}");

                        // 1. Скидаємо стан користувача
                        await _userStateService.SetStateAsync(callback.Message.Chat.Id, UserAction.None);

                        // 2. Редагуємо поточне повідомлення на головне меню
                        await ShowMainMenuAsync(callback.Message.Chat.Id, callbackMessageId);

                        // 3. Відповідаємо на callback
                        await _botClient.AnswerCallbackQuery(callback.Id);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error in back_to_menu: {ex.Message}");
                        await _botClient.AnswerCallbackQuery(callback.Id);
                    }

                    break;
                case "referral_rewards":
                    var callbackUser = await _manageService.GetUserByTelegramIdAsync(callbackChatId);
                    var refLang = GetUserLanguageCode(callbackUser);

                    if (callbackUser == null)
                    {
                        string notFoundMessage = T(
                            refLang,
                            "❌ <b>User Not Found</b>\n\nPlease complete your registration to access the referral program. 🚀",
                            "❌ <b>User Nahi Mila</b>\n\nReferral program ka istemal karne ke liye, pehle apna registration poora karein. 🚀",
                            "❌ <b>Пользователь не найден</b>\n\nПожалуйста, завершите регистрацию, чтобы получить доступ к реферальной программе. 🚀",
                            "❌ <b>کاربر پیدا نشد</b>\n\nبرای استفاده از برنامه دعوت، لطفاً ابتدا ثبت نام خود را کامل کنید. 🚀",
                            "❌ <b>لم يتم العثور على المستخدم</b>\n\nيرجى إكمال التسجيل للوصول إلى برنامج الإحالة. 🚀",
                            "❌ <b>未找到用户</b>\n\n请先完成注册后再使用邀请奖励计划。🚀");

                        await _botClient.SendMessage(
                            chatId: callbackChatId,
                            text: notFoundMessage,
                            parseMode: ParseMode.Html
                        );
                        break;
                    }

                    var referralLink = $"https://t.me/{_botUsername}?start={callbackUser.ReferralCode}";
                    var referralsCount = await _userStateService.GetReferralCountAsync(callbackUser.TelegramId);

                    // Отримуємо поточний відсоток реферальної винагороди
                    decimal userPercentage = _referralService.GetUserReferralPercentage(callbackUser.Id);
                    int displayPercentage = (int)(userPercentage * 100);

                    string referralText;
                    InlineKeyboardMarkup cKeyboard;

                    if (_localizationService.IsEnglish(refLang))
                    {
                        referralText =
                            "🎯 <b>Referral Rewards Program</b>\n\n" +
                            "✨ <i>Earn passive income by inviting friends!</i>\n\n" +
                            $"🔗 <b>Your Personal Referral Link:</b>\n<code>{referralLink}</code>\n\n" +
                            $"📊 <b>Your Referral Stats:</b>\n" +
                            $"• Invited Friends: <b>{referralsCount}</b>\n" +
                            $"• Commission Rate: <b>{displayPercentage}%</b> of their investment profits\n\n" +
                            "💡 <b>How it works:</b>\n" +
                            "1. Share your link with friends\n" +
                            "2. They register using your link\n" +
                            $"3. You earn {displayPercentage}% from all their investment profits\n" +
                            "4. Rewards are credited automatically\n\n" +
                            "🚀 <i>Start earning today!</i>";

                        cKeyboard = new InlineKeyboardMarkup(new[]
                        {
                            new[]
                            {
                                InlineKeyboardButton.WithCopyText("📋 Copy Referral Link", referralLink),
                                InlineKeyboardButton.WithUrl("📤 Share via Telegram",
                                    $"tg://msg_url?url={Uri.EscapeDataString(referralLink)}&text={Uri.EscapeDataString("Join me on StarQuantum.AI - AI-powered investments! 🚀")}")
                            },
                            new[]
                            {
                                InlineKeyboardButton.WithCallbackData("🔙 Back to Menu", "back_to_menu")
                            }
                        });
                    }
                    else if (_localizationService.NormalizeCode(refLang) == BotLanguageCodes.Hinglish)
                    {
                        referralText =
                            "🎯 <b>Referral Rewards Program</b>\n\n" +
                            "✨ <i>Dosto ko invite karke passive income kamao!</i>\n\n" +
                            $"🔗 <b>Aapka Personal Referral Link:</b>\n<code>{referralLink}</code>\n\n" +
                            $"📊 <b>Aapke Referral Stats:</b>\n" +
                            $"• Invited Friends: <b>{referralsCount}</b>\n" +
                            $"• Commission Rate: Unki investment profits ka <b>{displayPercentage}%</b>\n\n" +
                            "💡 <b>Kaise kaam karta hai:</b>\n" +
                            "1. Apna link dosto ke saath share karo\n" +
                            "2. Wo aapke link se register karenge\n" +
                            $"3. Aap unki investment profits se {displayPercentage}% kamayein\n" +
                            "4. Rewards automatically credit honge\n\n" +
                            "🚀 <i>Aaj hi earning shuru karo!</i>";

                        cKeyboard = new InlineKeyboardMarkup(new[]
                        {
                            new[]
                            {
                                InlineKeyboardButton.WithCopyText("📋 Referral Link Copy Karo", referralLink),
                                InlineKeyboardButton.WithUrl("📤 Telegram Pe Share Karo",
                                    $"tg://msg_url?url={Uri.EscapeDataString(referralLink)}&text={Uri.EscapeDataString("StarQuantum.AI mein mere saath join karo - AI-powered investments! 🚀")}")
                            },
                            new[]
                            {
                                InlineKeyboardButton.WithCallbackData("🔙 Wapas Menu me", "back_to_menu")
                            }
                        });
                    }
                    else
                    {
                        referralText = T(
                            refLang,
                            string.Empty,
                            string.Empty,
                            "🎯 <b>Реферальная программа</b>\n\n✨ <i>Получайте пассивный доход, приглашая друзей!</i>\n\n🔗 <b>Ваша персональная ссылка:</b>\n<code>{0}</code>\n\n📊 <b>Ваша статистика:</b>\n• Приглашено друзей: <b>{1}</b>\n• Комиссия: <b>{2}%</b> от прибыли их инвестиций\n\n💡 <b>Как это работает:</b>\n1. Поделитесь ссылкой с друзьями\n2. Они зарегистрируются по вашей ссылке\n3. Вы получите {2}% от прибыли их инвестиций\n4. Награды начисляются автоматически\n\n🚀 <i>Начните зарабатывать уже сегодня!</i>",
                            "🎯 <b>برنامه پاداش دعوت</b>\n\n✨ <i>با دعوت دوستان درآمد غیرفعال کسب کنید!</i>\n\n🔗 <b>لینک دعوت شخصی شما:</b>\n<code>{0}</code>\n\n📊 <b>آمار دعوت شما:</b>\n• دوستان دعوت شده: <b>{1}</b>\n• نرخ کمیسیون: <b>{2}%</b> از سود سرمایه گذاری آن ها\n\n💡 <b>نحوه کار:</b>\n1. لینک خود را با دوستان به اشتراک بگذارید\n2. آن ها با لینک شما ثبت نام می کنند\n3. شما {2}% از سود سرمایه گذاری آن ها را دریافت می کنید\n4. پاداش ها به صورت خودکار واریز می شوند\n\n🚀 <i>همین امروز کسب درآمد را شروع کنید!</i>",
                            "🎯 <b>برنامج مكافآت الإحالة</b>\n\n✨ <i>اكسب دخلاً سلبياً من خلال دعوة الأصدقاء!</i>\n\n🔗 <b>رابط الإحالة الشخصي الخاص بك:</b>\n<code>{0}</code>\n\n📊 <b>إحصاءات الإحالة الخاصة بك:</b>\n• الأصدقاء المدعوون: <b>{1}</b>\n• نسبة العمولة: <b>{2}%</b> من أرباح استثماراتهم\n\n💡 <b>كيف يعمل:</b>\n1. شارك الرابط مع الأصدقاء\n2. يسجلون باستخدام رابطك\n3. تحصل على {2}% من أرباح استثماراتهم\n4. تتم إضافة المكافآت تلقائياً\n\n🚀 <i>ابدأ الربح اليوم!</i>",
                            "🎯 <b>邀请奖励计划</b>\n\n✨ <i>邀请朋友，赚取被动收入！</i>\n\n🔗 <b>您的专属邀请链接：</b>\n<code>{0}</code>\n\n📊 <b>您的邀请统计：</b>\n• 已邀请好友：<b>{1}</b>\n• 佣金比例：其投资利润的 <b>{2}%</b>\n\n💡 <b>运作方式：</b>\n1. 将链接分享给朋友\n2. 他们通过您的链接注册\n3. 您将获得其投资利润的 {2}%\n4. 奖励将自动发放\n\n🚀 <i>立即开始赚取收益！</i>");
                        referralText = string.Format(referralText, referralLink, referralsCount, displayPercentage);

                        cKeyboard = new InlineKeyboardMarkup(new[]
                        {
                            new[]
                            {
                                InlineKeyboardButton.WithCopyText(T(refLang, "📋 Copy Referral Link", "📋 Referral Link Copy Karo", "📋 Скопировать ссылку", "📋 کپی لینک دعوت", "📋 نسخ رابط الإحالة", "📋 复制邀请链接"), referralLink),
                                InlineKeyboardButton.WithUrl(T(refLang, "📤 Share via Telegram", "📤 Telegram Pe Share Karo", "📤 Поделиться в Telegram", "📤 اشتراک در تلگرام", "📤 المشاركة عبر Telegram", "📤 通过 Telegram 分享"),
                                    $"tg://msg_url?url={Uri.EscapeDataString(referralLink)}&text={Uri.EscapeDataString(T(refLang, "Join me on StarQuantum.AI - AI-powered investments! 🚀", "StarQuantum.AI mein mere saath join karo - AI-powered investments! 🚀", "Присоединяйтесь ко мне в StarQuantum.AI - инвестиции на базе AI! 🚀", "با من در StarQuantum.AI همراه شوید - سرمایه گذاری با هوش مصنوعی! 🚀", "انضم إليّ في StarQuantum.AI - استثمارات مدعومة بالذكاء الاصطناعي! 🚀", "加入我一起体验 StarQuantum.AI - AI 驱动投资！🚀"))}")
                            },
                            new[]
                            {
                                InlineKeyboardButton.WithCallbackData(T(refLang, "🔙 Back to Menu", "🔙 Wapas Menu me", "🔙 Назад в меню", "🔙 بازگشت به منو", "🔙 العودة إلى القائمة", "🔙 返回菜单"), "back_to_menu")
                            }
                        });
                    }

                    await EditOrSendAsync(
                        callbackChatId,
                        callbackMessageId,
                        referralText,
                        parseMode: ParseMode.Html,
                        replyMarkup: cKeyboard
                    );
                    break;

                case "my_profile":
                    try
                    {
                        Console.WriteLine($"Profile requested by {callback.From.Id}");

                        // Показуємо основний профіль (редагуємо поточне повідомлення)
                        await _profileService.ShowProfileAsync(callback.Message.Chat.Id, true, callbackMessageId);

                        var myProfUser = await _manageService.GetUserByTelegramIdAsync(callback.Message.Chat.Id);
                        var profLang = GetUserLanguageCode(myProfUser);

                        // Перевіряємо чи є активні інвестиції
                        var hasActiveInvestments = await _botDbContext.Investments
                            .AsNoTracking()
                            .AnyAsync(i => i.User.TelegramId == callback.Message.Chat.Id && i.IsActive);

                        if (hasActiveInvestments)
                        {
                            try
                            {
                                // ✅ Ініціалізуємо торгівельну сесію на основі активних інвестицій
                                await _tradingSimulationService.InitializeChartSessionAsync(callback.Message.Chat.Id);

                                // ✅ Генеруємо свічки (реальна динаміка прибутку)
                                var candles =
                                    await _tradingSimulationService.GenerateChartCandlesAsync(callback.Message.Chat.Id);

                                // ✅ Отримуємо метрики для графіку
                                var metrics =
                                    await _tradingSimulationService.GetChartMetricsAsync(callback.Message.Chat.Id);

                                // ✅ Рендеруємо графік
                                var language = _localizationService.NormalizeCode(profLang);
                                var img = await _chartRendererService.RenderTradingChartAsync(candles, metrics,
                                    language);

                                await using var ms = new System.IO.MemoryStream(img);

// Формуємо підпис з інформацією про прибуток
                                                                var chartCaption = T(
                                                                        profLang,
                                                                        $"📊 <b>Investment Performance Chart</b>\n\n📈 <b>Real-Time Profit Tracking</b>\n\n💰 <b>Current Price:</b> ${metrics.CurrentPrice:F2}\n💎 <b>Profit:</b> ${Math.Abs(metrics.ProfitLoss):F2}\n\n💡 <i>Green candles = Profit growth | Red candles = Market fluctuation</i>",
                                                                        $"📊 <b>Investment Performance Chart</b>\n\n📈 <b>Real-Time Profit Tracking</b>\n\n💰 <b>Current Price:</b> ${metrics.CurrentPrice:F2}\n📊 <b>Price Change:</b> {(metrics.ChangePercent >= 0 ? "+" : "")}{metrics.ChangePercent:F2}%\n💹 <b>Balance Impact:</b> {(metrics.BalanceChangePercent >= 0 ? "+" : "")}{metrics.BalanceChangePercent:F2}%\n\n💎 <b>Profit/Loss:</b> ${Math.Abs(metrics.ProfitLoss):F2}\n\n💡 <i>Zelene Mombattiyaan = Profit | Laal Mombattiyaan = Fluctuation</i>",
                                                                        $"📊 <b>График эффективности инвестиций</b>\n\n📈 <b>Отслеживание прибыли в реальном времени</b>\n\n💰 <b>Текущая цена:</b> ${metrics.CurrentPrice:F2}\n📊 <b>Изменение цены:</b> {(metrics.ChangePercent >= 0 ? "+" : "")}{metrics.ChangePercent:F2}%\n💹 <b>Влияние на баланс:</b> {(metrics.BalanceChangePercent >= 0 ? "+" : "")}{metrics.BalanceChangePercent:F2}%\n\n💎 <b>Прибыль/убыток:</b> ${Math.Abs(metrics.ProfitLoss):F2}\n\n💡 <i>Зеленые свечи = рост прибыли | Красные свечи = колебания рынка</i>",
                                                                        $"📊 <b>نمودار عملکرد سرمایه گذاری</b>\n\n📈 <b>رهگیری سود در زمان واقعی</b>\n\n💰 <b>قیمت فعلی:</b> ${metrics.CurrentPrice:F2}\n📊 <b>تغییر قیمت:</b> {(metrics.ChangePercent >= 0 ? "+" : "")}{metrics.ChangePercent:F2}%\n💹 <b>تاثیر بر موجودی:</b> {(metrics.BalanceChangePercent >= 0 ? "+" : "")}{metrics.BalanceChangePercent:F2}%\n\n💎 <b>سود/زیان:</b> ${Math.Abs(metrics.ProfitLoss):F2}\n\n💡 <i>کندل سبز = رشد سود | کندل قرمز = نوسان بازار</i>",
                                                                        $"📊 <b>مخطط أداء الاستثمار</b>\n\n📈 <b>تتبع الربح في الوقت الفعلي</b>\n\n💰 <b>السعر الحالي:</b> ${metrics.CurrentPrice:F2}\n📊 <b>تغير السعر:</b> {(metrics.ChangePercent >= 0 ? "+" : "")}{metrics.ChangePercent:F2}%\n💹 <b>تأثير الرصيد:</b> {(metrics.BalanceChangePercent >= 0 ? "+" : "")}{metrics.BalanceChangePercent:F2}%\n\n💎 <b>الربح/الخسارة:</b> ${Math.Abs(metrics.ProfitLoss):F2}\n\n💡 <i>الشموع الخضراء = نمو الربح | الشموع الحمراء = تقلبات السوق</i>",
                                                                        $"📊 <b>投资表现图表</b>\n\n📈 <b>实时收益追踪</b>\n\n💰 <b>当前价格：</b>${metrics.CurrentPrice:F2}\n📊 <b>价格变动：</b>{(metrics.ChangePercent >= 0 ? "+" : "")}{metrics.ChangePercent:F2}%\n💹 <b>余额影响：</b>{(metrics.BalanceChangePercent >= 0 ? "+" : "")}{metrics.BalanceChangePercent:F2}%\n\n💎 <b>盈亏：</b>${Math.Abs(metrics.ProfitLoss):F2}\n\n💡 <i>绿色蜡烛 = 利润增长 | 红色蜡烛 = 市场波动</i>");

// ✅ Додаємо кнопки під графік
                                var chartButtons = new InlineKeyboardMarkup(new[]
                                {
                                    new[]
                                    {
                                        InlineKeyboardButton.WithCallbackData(
                                            T(profLang, "🔙 Back to Menu", "🔙 Wapas Menu", "🔙 Назад в меню", "🔙 بازگشت به منو", "🔙 العودة إلى القائمة", "🔙 返回菜单"),
                                            "back_to_menu")
                                    }
                                });

                                await _botClient.SendPhoto(
                                    callback.Message.Chat.Id,
                                    Telegram.Bot.Types.InputFile.FromStream(ms, "investment_chart.png"),
                                    caption: chartCaption,
                                    parseMode: ParseMode.Html,
                                    replyMarkup: chartButtons, // ✅ Додано це поле
                                    cancellationToken: cancellationToken);

                                // ✅ Очищаємо сесію після використання (опціонально)
                                // _investmentTradingService.ClearSession(callback.Message.Chat.Id);
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine($"Error rendering investment chart: {ex.Message}");
                                // Graceful fallback - показуємо тільки текст
                                var metrics =
                                    await _tradingSimulationService.GetChartMetricsAsync(callback.Message.Chat.Id);
                                                                var fallbackCaption = T(
                                                                        profLang,
                                                                        $"📊 <b>Investment Status</b>\n💰 Price: ${metrics.CurrentPrice:F2}\n📈 Change: {metrics.ChangePercent:F2}%\n",
                                                                        $"📊 <b>Investment Status</b>\n💰 Price: ${metrics.CurrentPrice:F2}\n📈 Change: {metrics.ChangePercent:F2}%\n",
                                                                        $"📊 <b>Статус инвестиции</b>\n💰 Цена: ${metrics.CurrentPrice:F2}\n📈 Изменение: {metrics.ChangePercent:F2}%\n",
                                                                        $"📊 <b>وضعیت سرمایه گذاری</b>\n💰 قیمت: ${metrics.CurrentPrice:F2}\n📈 تغییر: {metrics.ChangePercent:F2}%\n",
                                                                        $"📊 <b>حالة الاستثمار</b>\n💰 السعر: ${metrics.CurrentPrice:F2}\n📈 التغير: {metrics.ChangePercent:F2}%\n",
                                                                        $"📊 <b>投资状态</b>\n💰 价格：${metrics.CurrentPrice:F2}\n📈 变化：{metrics.ChangePercent:F2}%\n");

                                await _botClient.SendMessage(
                                    callback.Message.Chat.Id,
                                    fallbackCaption,
                                    parseMode: ParseMode.Html,
                                    cancellationToken: cancellationToken);
                            }
                        }

                        await _botClient.AnswerCallbackQuery(callback.Id);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error in my_profile: {ex.Message}");
                        await _botClient.AnswerCallbackQuery(callback.Id);
                    }

                    break;

                case "support_help":
                    var supportUser = await _manageService.GetUserByTelegramIdAsync(callbackChatId);
                                        var supportLang = GetUserLanguageCode(supportUser);

                                        var supportText = T(
                                                supportLang,
                                                "🎯 *SUPPORT CENTER*\n\n🛟 *Need Assistance?*\nOur team is here to help you succeed!\n\n📚 *Quick Start Guide*\n\n💰 *1. Make a Deposit*\n• Send USDT (TRC20 network only)\n• Minimum: 1 USDT\n• Confirm your transaction\n\n⏳ *2. Wait for Confirmation*\n• Usually takes 15-45 minutes\n• Funds will appear in your profile\n\n📊 *3. Track Your Portfolio*\n• Monitor investments in real-time\n• View projected earnings\n• Check referral rewards\n\n🚀 *4. Start Investing*\n• Choose investment amount\n• Select duration (2-14 days)\n• Earn daily profits\n\n📞 *Contact Support*\n\n💬 *Telegram:* @ShantiAIWE\n📧 *Email:* support@shanti.ai\n⏰ *Response Time:* < 24 hours\n\n🔒 *Security Guarantee*\n• Your funds remain under your control\n• We never access your wallet directly\n• All transactions are transparent\n• 256-bit encryption protection",
                                                "🎯 *SUPPORT CENTER*\n\n🛟 *Madad Chahiye?*\nHamari team aapki madad ke liye yaha hai!\n\n📚 *Quick Start Guide*\n\n💰 *1. Deposit Karein*\n• USDT bhejein (sirf TRC20 network)\n• Minimum: 1 USDT\n• Apna transaction confirm karein\n\n⏳ *2. Confirmation Ka Intezar Karein*\n• Aam taur par 15-45 minutes lagte hain\n• Funds aapke profile mein dikhenge\n\n📊 *3. Apna Portfolio Track Karein*\n• Real-time mein investments dekhein\n• Projected earnings check karein\n• Referral rewards dekhein\n\n🚀 *4. Investing Shuru Karein*\n• Investment amount chunein\n• Duration select karein (2-14 days)\n• Daily profit kamayein\n\n📞 *Support Se Contact Karein*\n\n💬 *Telegram:* @ShantiAIWE\n📧 *Email:* support@shanti.ai\n⏰ *Response Time:* < 24 hours\n\n🔒 *Security Guarantee*\n• Aapke funds aapke control mein rahte hain\n• Hum kabhi bhi aapka wallet access nahi karte\n• Sabhi transactions transparent hain\n• 256-bit encryption protection",
                                                "🎯 *ЦЕНТР ПОДДЕРЖКИ*\n\n🛟 *Нужна помощь?*\nНаша команда готова помочь вам!\n\n📚 *Краткое руководство*\n\n💰 *1. Пополнение*\n• Отправьте USDT (только сеть TRC20)\n• Минимум: 1 USDT\n• Подтвердите транзакцию\n\n⏳ *2. Ожидание подтверждения*\n• Обычно занимает 15-45 минут\n• Средства появятся в вашем профиле\n\n📊 *3. Отслеживание портфеля*\n• Следите за инвестициями в реальном времени\n• Просматривайте прогнозируемый доход\n• Проверяйте реферальные награды\n\n🚀 *4. Начните инвестировать*\n• Выберите сумму инвестиции\n• Выберите срок (2-14 дней)\n• Получайте ежедневную прибыль\n\n📞 *Связаться с поддержкой*\n\n💬 *Telegram:* @ShantiAIWE\n📧 *Email:* support@shanti.ai\n⏰ *Время ответа:* < 24 часов\n\n🔒 *Гарантия безопасности*\n• Ваши средства остаются под вашим контролем\n• Мы никогда не получаем прямой доступ к вашему кошельку\n• Все транзакции прозрачны\n• Защита 256-битным шифрованием",
                                                "🎯 *مرکز پشتیبانی*\n\n🛟 *به کمک نیاز دارید؟*\nتیم ما اینجاست تا به شما کمک کند!\n\n📚 *راهنمای سریع شروع*\n\n💰 *1. واریز انجام دهید*\n• USDT ارسال کنید (فقط شبکه TRC20)\n• حداقل: 1 USDT\n• تراکنش خود را تایید کنید\n\n⏳ *2. منتظر تایید بمانید*\n• معمولاً 15 تا 45 دقیقه طول می کشد\n• وجه در پروفایل شما نمایش داده می شود\n\n📊 *3. پرتفوی خود را دنبال کنید*\n• سرمایه گذاری ها را به صورت لحظه ای مشاهده کنید\n• سود پیش بینی شده را بررسی کنید\n• پاداش های دعوت را ببینید\n\n🚀 *4. سرمایه گذاری را شروع کنید*\n• مبلغ سرمایه گذاری را انتخاب کنید\n• مدت زمان را انتخاب کنید (2 تا 14 روز)\n• سود روزانه دریافت کنید\n\n📞 *تماس با پشتیبانی*\n\n💬 *Telegram:* @ShantiAIWE\n📧 *Email:* support@shanti.ai\n⏰ *زمان پاسخ:* کمتر از 24 ساعت\n\n🔒 *تضمین امنیت*\n• دارایی شما تحت کنترل خودتان باقی می ماند\n• ما هرگز مستقیماً به کیف پول شما دسترسی نداریم\n• همه تراکنش ها شفاف هستند\n• محافظت با رمزگذاری 256 بیتی",
                                                "🎯 *مركز الدعم*\n\n🛟 *هل تحتاج إلى مساعدة؟*\nفريقنا هنا لمساعدتك على النجاح!\n\n📚 *دليل البدء السريع*\n\n💰 *1. قم بالإيداع*\n• أرسل USDT (شبكة TRC20 فقط)\n• الحد الأدنى: 1 USDT\n• أكد العملية\n\n⏳ *2. انتظر التأكيد*\n• يستغرق ذلك عادة من 15 إلى 45 دقيقة\n• ستظهر الأموال في ملفك الشخصي\n\n📊 *3. تابع محفظتك*\n• راقب الاستثمارات في الوقت الفعلي\n• اعرض الأرباح المتوقعة\n• تحقق من مكافآت الإحالة\n\n🚀 *4. ابدأ الاستثمار*\n• اختر مبلغ الاستثمار\n• اختر المدة (من 2 إلى 14 يوماً)\n• احصل على أرباح يومية\n\n📞 *التواصل مع الدعم*\n\n💬 *Telegram:* @ShantiAIWE\n📧 *Email:* support@shanti.ai\n⏰ *زمن الاستجابة:* أقل من 24 ساعة\n\n🔒 *ضمان الأمان*\n• أموالك تبقى تحت سيطرتك\n• نحن لا نصل إلى محفظتك مباشرة أبداً\n• جميع المعاملات شفافة\n• حماية بتشفير 256 بت",
                                                "🎯 *支持中心*\n\n🛟 *需要帮助吗？*\n我们的团队随时准备帮助您！\n\n📚 *快速入门指南*\n\n💰 *1. 进行充值*\n• 发送 USDT（仅支持 TRC20 网络）\n• 最低金额：1 USDT\n• 确认您的交易\n\n⏳ *2. 等待确认*\n• 通常需要 15-45 分钟\n• 资金会显示在您的个人资料中\n\n📊 *3. 追踪您的投资组合*\n• 实时查看投资情况\n• 查看预计收益\n• 查看邀请奖励\n\n🚀 *4. 开始投资*\n• 选择投资金额\n• 选择期限（2-14 天）\n• 获得每日收益\n\n📞 *联系支持*\n\n💬 *Telegram:* @ShantiAIWE\n📧 *Email:* support@shanti.ai\n⏰ *响应时间:* < 24 小时\n\n🔒 *安全保障*\n• 您的资金始终由您自己控制\n• 我们绝不会直接访问您的钱包\n• 所有交易完全透明\n• 256 位加密保护");

                    var supportKeyboard2 = new InlineKeyboardMarkup(new[]
                    {
                        new[]
                        {
                            InlineKeyboardButton.WithUrl(
                                T(supportLang, "💬 Contact Support", "💬 Support Se Contact", "💬 Связаться с поддержкой", "💬 تماس با پشتیبانی", "💬 التواصل مع الدعم", "💬 联系支持"),
                                "https://t.me/ShantiAIWE")
                        },
                        new[]
                        {
                            InlineKeyboardButton.WithCallbackData(
                                T(supportLang, "🔙 Back to Menu", "🔙 Wapas Menu", "🔙 Назад в меню", "🔙 بازگشت به منو", "🔙 العودة إلى القائمة", "🔙 返回菜单"),
                                "back_to_menu")
                        }
                    });

                    await EditOrSendAsync(
                        callbackChatId,
                        callbackMessageId,
                        supportText,
                        parseMode: ParseMode.Markdown,
                        replyMarkup: supportKeyboard2
                    );
                    break;
                case "deposit":
                {
                    var depositUser = await _manageService.GetUserByTelegramIdAsync(callbackChatId);
                    var depositLang = GetUserLanguageCode(depositUser);

                    // Перевірка авторизації
                    if (depositUser == null || !depositUser.IsAuthorized)
                    {
                        string authMessage = T(
                            depositLang,
                            "🔐 <b>Registration Required</b>\n\nTo make a deposit, please complete your registration first. It's quick and easy! 🚀",
                            "🔐 <b>Registration Zaroori Hai</b>\n\nDeposit karne ke liye, pehle apna registration poora karein. Yeh jaldi aur aasan hai! 🚀",
                            "🔐 <b>Требуется регистрация</b>\n\nЧтобы пополнить счет, пожалуйста, сначала завершите регистрацию. Это быстро и просто! 🚀",
                            "🔐 <b>ثبت نام لازم است</b>\n\nبرای واریز، لطفاً ابتدا ثبت نام خود را کامل کنید. سریع و آسان است! 🚀",
                            "🔐 <b>التسجيل مطلوب</b>\n\nلإجراء إيداع، يرجى إكمال التسجيل أولاً. الأمر سريع وسهل! 🚀",
                            "🔐 <b>需要完成注册</b>\n\n如需充值，请先完成注册。过程快速且简单！🚀");

                        await _botClient.SendMessage(
                            chatId: callbackChatId,
                            text: authMessage,
                            parseMode: ParseMode.Html
                        );
                        return;
                    }

                    await _userStateService.SetStateAsync(callbackChatId, UserAction.WaitingDepositAmount);

                    // Професійне та дружнє повідомлення про депозит
                    string depositMessage = T(
                        depositLang,
                        "💰 <b>Make a Deposit</b>\n\n✨ <i>Ready to grow your investment?</i>\n\nPlease enter the amount you'd like to deposit:\n\n• <b>Minimum:</b> 1 USDT\n• <b>Network:</b> TRC20 (TRON)\n• <b>Currency:</b> USDT only\n\n❓ <i>Need help? Use the button below.</i>",
                        "💰 <b>Deposit Karein</b>\n\n✨ <i>Apne nivesh ko badhane ke liye taiyar?</i>\n\nKripya woh raash daalen jise aap deposit karna chahte hain:\n\n• <b>Minimum:</b> 1 USDT\n• <b>Network:</b> TRC20 (TRON)\n• <b>Currency:</b> Sirf USDT\n\n❓ <i>Madad chahiye? Neeche button use karein.</i>",
                        "💰 <b>Пополнение</b>\n\n✨ <i>Готовы увеличить свои инвестиции?</i>\n\nВведите сумму пополнения:\n\n• <b>Минимум:</b> 1 USDT\n• <b>Сеть:</b> TRC20 (TRON)\n• <b>Валюта:</b> только USDT\n\n❓ <i>Нужна помощь? Используйте кнопку ниже.</i>",
                        "💰 <b>واریز انجام دهید</b>\n\n✨ <i>آماده رشد سرمایه گذاری خود هستید؟</i>\n\nلطفاً مبلغی را که می خواهید واریز کنید وارد کنید:\n\n• <b>حداقل:</b> 1 USDT\n• <b>شبکه:</b> TRC20 (TRON)\n• <b>ارز:</b> فقط USDT\n\n❓ <i>به کمک نیاز دارید؟ از دکمه زیر استفاده کنید.</i>",
                        "💰 <b>قم بالإيداع</b>\n\n✨ <i>هل أنت مستعد لتنمية استثمارك؟</i>\n\nيرجى إدخال المبلغ الذي ترغب في إيداعه:\n\n• <b>الحد الأدنى:</b> 1 USDT\n• <b>الشبكة:</b> TRC20 (TRON)\n• <b>العملة:</b> USDT فقط\n\n❓ <i>تحتاج إلى مساعدة؟ استخدم الزر أدناه.</i>",
                        "💰 <b>进行充值</b>\n\n✨ <i>准备开始增长您的投资了吗？</i>\n\n请输入您想充值的金额：\n\n• <b>最低金额：</b>1 USDT\n• <b>网络：</b>TRC20 (TRON)\n• <b>币种：</b>仅限 USDT\n\n❓ <i>需要帮助？请使用下方按钮。</i>");

                    // Створюємо клавіатуру з кнопкою підтримки
                    var supportKeyboard = new InlineKeyboardMarkup(new[]
                    {
                        new[]
                        {
                            InlineKeyboardButton.WithCallbackData(
                                T(depositLang, "🛟 Contact Support", "🛟 Support Se Sampark Karein", "🛟 Связаться с поддержкой", "🛟 تماس با پشتیبانی", "🛟 التواصل مع الدعم", "🛟 联系支持"),
                                "support_help")
                        },
                        new[]
                        {
                            InlineKeyboardButton.WithCallbackData(
                                T(depositLang, "🔙 Back to Menu", "🔙 Wapas Menu", "🔙 Назад в меню", "🔙 بازگشت به منو", "🔙 العودة إلى القائمة", "🔙 返回菜单"),
                                "back_to_menu")
                        }
                    });

                    // Редагуємо повідомлення про депозит
                    await EditOrSendAsync(
                        callbackChatId,
                        callbackMessageId,
                        depositMessage,
                        parseMode: ParseMode.Html,
                        replyMarkup: supportKeyboard
                    );
                    break;
                }
                case "confirm_payment":
                    var confirmUser = await _manageService.GetUserByTelegramIdAsync(callbackChatId);
                    var confirmLang = GetUserLanguageCode(confirmUser);

                    if (_pendingDeposits.TryGetValue(callbackChatId, out var depositAmount))
                    {
                        await _operationService.CreateDepositRequestAsync(callbackChatId, depositAmount);
                        _pendingDeposits.Remove(callbackChatId);

                        // Language-specific messages
                        var successMessage = T(
                            confirmLang,
                            $"🎉 <b>Deposit Successful!</b>\n\n✅ <b>Amount:</b> {depositAmount} USDT\n\n⏳ <b>Status:</b> Processing...\nYour deposit request has been received and is being processed.\n\n📋 <b>What happens next?</b>\n• We'll verify the transaction\n• Funds will be added to your balance\n• You'll receive a confirmation message\n\n⏰ <i>Usually takes 15-45 minutes</i>\n\nThank you for choosing StarQuantum.AI! 💙",
                            $"🎉 <b>Deposit Safal!</b>\n\n✅ <b>Raash:</b> {depositAmount} USDT\n\n⏳ <b>Status:</b> Processing...\nAapka deposit request receive ho gaya hai aur process ho raha hai.\n\n📋 <b>Aage kya hoga?</b>\n• Hum transaction verify karenge\n• Funds aapke balance mein add honge\n• Aapko confirmation message milega\n\n⏰ <i>Aam taur par 15-45 minute lagte hain</i>\n\nStarQuantum.AI choose karne ke liye dhanyavaad! 💙",
                            $"🎉 <b>Пополнение успешно!</b>\n\n✅ <b>Сумма:</b> {depositAmount} USDT\n\n⏳ <b>Статус:</b> Обрабатывается...\nВаш запрос на пополнение получен и обрабатывается.\n\n📋 <b>Что дальше?</b>\n• Мы проверим транзакцию\n• Средства будут добавлены на ваш баланс\n• Вы получите подтверждение\n\n⏰ <i>Обычно это занимает 15-45 минут</i>\n\nСпасибо, что выбрали StarQuantum.AI! 💙",
                            $"🎉 <b>واریز با موفقیت ثبت شد!</b>\n\n✅ <b>مبلغ:</b> {depositAmount} USDT\n\n⏳ <b>وضعیت:</b> در حال پردازش...\nدرخواست واریز شما دریافت شد و در حال بررسی است.\n\n📋 <b>مرحله بعدی چیست؟</b>\n• تراکنش را بررسی می کنیم\n• وجه به موجودی شما اضافه می شود\n• پیام تایید دریافت خواهید کرد\n\n⏰ <i>معمولاً 15 تا 45 دقیقه زمان می برد</i>\n\nاز اینکه StarQuantum.AI را انتخاب کردید سپاسگزاریم! 💙",
                            $"🎉 <b>تم تسجيل الإيداع بنجاح!</b>\n\n✅ <b>المبلغ:</b> {depositAmount} USDT\n\n⏳ <b>الحالة:</b> قيد المعالجة...\nتم استلام طلب الإيداع الخاص بك ويجري العمل عليه.\n\n📋 <b>ماذا بعد؟</b>\n• سنتحقق من المعاملة\n• ستتم إضافة الأموال إلى رصيدك\n• ستتلقى رسالة تأكيد\n\n⏰ <i>يستغرق ذلك عادة من 15 إلى 45 دقيقة</i>\n\nشكراً لاختيارك StarQuantum.AI! 💙",
                            $"🎉 <b>充值成功提交！</b>\n\n✅ <b>金额：</b>{depositAmount} USDT\n\n⏳ <b>状态：</b>处理中...\n您的充值请求已收到，正在处理中。\n\n📋 <b>接下来会发生什么？</b>\n• 我们将验证交易\n• 资金将添加到您的余额\n• 您将收到确认消息\n\n⏰ <i>通常需要 15-45 分钟</i>\n\n感谢您选择 StarQuantum.AI！💙");
                        var backButtonText = T(confirmLang, "🏠 Back to Menu", "🏠 Wapas Menu", "🏠 Назад в меню", "🏠 بازگشت به منو", "🏠 العودة إلى القائمة", "🏠 返回菜单");

                        var buttons = new InlineKeyboardMarkup(new[]
                        {
                            new[]
                            {
                                InlineKeyboardButton.WithCallbackData("📊 My Profile", "my_profile")
                            },
                            new[]
                            {
                                InlineKeyboardButton.WithCallbackData(backButtonText, "back_to_menu")
                            }
                        });

                        await _botClient.SendMessage(
                            chatId: callbackChatId,
                            text: successMessage,
                            parseMode: ParseMode.Html,
                            replyMarkup: buttons
                        );
                    }
                    else
                    {
                        string errorMessage = T(
                            confirmLang,
                            "⚠️ <b>Deposit Not Found</b>\n\nWe couldn't find your pending deposit. Please try making a deposit again or contact support if the issue persists.",
                            "⚠️ <b>Deposit Nahi Mila</b>\n\nHum aapka pending deposit nahi dhundh paaye. Kripya phir se deposit karne ka prayas karein ya agar problem bani rahe to support se sampark karein.",
                            "⚠️ <b>Пополнение не найдено</b>\n\nМы не нашли ожидающее пополнение. Попробуйте снова или свяжитесь с поддержкой, если проблема сохранится.",
                            "⚠️ <b>واریز پیدا نشد</b>\n\nما واریز در انتظار شما را پیدا نکردیم. لطفاً دوباره تلاش کنید یا در صورت ادامه مشکل با پشتیبانی تماس بگیرید.",
                            "⚠️ <b>لم يتم العثور على الإيداع</b>\n\nلم نتمكن من العثور على الإيداع المعلق. يرجى المحاولة مرة أخرى أو التواصل مع الدعم إذا استمرت المشكلة.",
                            "⚠️ <b>未找到待处理充值</b>\n\n我们未找到您的待处理充值。请重试，如问题仍存在请联系支持。 ");

                        var errorButtons = new InlineKeyboardMarkup(new[]
                        {
                            new[]
                            {
                                InlineKeyboardButton.WithCallbackData(T(confirmLang, "💳 Try Deposit Again", "💳 Phir Se Deposit Karo", "💳 Повторить пополнение", "💳 دوباره واریز کنید", "💳 حاول الإيداع مرة أخرى", "💳 再次充值"), "deposit"),
                                InlineKeyboardButton.WithCallbackData(T(confirmLang, "🛟 Contact Support", "🛟 Support Se Sampark Karein", "🛟 Связаться с поддержкой", "🛟 تماس با پشتیبانی", "🛟 التواصل مع الدعم", "🛟 联系支持"), "support_help")
                            }
                        });

                        await _botClient.SendMessage(
                            chatId: callbackChatId,
                            text: errorMessage,
                            parseMode: ParseMode.Html,
                            replyMarkup: errorButtons
                        );
                    }

                    break;
                case "admin_manage_requests":
                {
                    // Перевіряємо, чи користувач адмін
                    var adminUser = await _manageService.GetUserByTelegramIdAsync(callbackChatId);
                    if (adminUser?.Role != UserRole.Admin)
                    {
                        await bot.SendMessage(callbackChatId, "❌ Access denied.");
                        break;
                    }

                    // Отримуємо заявки на депозит зі статусом "Pending"
                    var requests = await _botDbContext.DepositRequests
                        .Include(dr => dr.User)
                        .Where(dr => dr.Status == "Pending")
                        .ToListAsync();

                    if (!requests.Any())
                    {
                        await bot.SendMessage(callbackChatId, "No pending deposit requests.");
                        break;
                    }

                    // Відправляємо кожну заявку окремо з кнопками Approve/Reject
                    foreach (var req in requests)
                    {
                        var text = $"🆔 Request #{req.Id}\n" +
                                   $"👤 From: @{req.User.Username ?? req.User.TelegramId.ToString()}\n" +
                                   $"💰 Amount: {req.Amount} USDT\n" +
                                   $"📅 Created At: {req.CreatedAt:dd.MM.yyyy HH:mm}\n" +
                                   $"🔖 Status: {req.Status}";

                        var buttons = new InlineKeyboardMarkup(new[]
                        {
                            new[]
                            {
                                InlineKeyboardButton.WithCallbackData("✅ Approve", $"approve_deposit_{req.Id}"),
                                InlineKeyboardButton.WithCallbackData("❌ Reject", $"reject_deposit_{req.Id}")
                            }
                        });

                        await bot.SendMessage(callbackChatId, text, replyMarkup: buttons);
                    }

                    break;
                }
                case var d when d.StartsWith("approve_deposit_"):
                {
                    var adminUser = await _manageService.GetUserByTelegramIdAsync(callbackChatId);
                    if (adminUser?.Role != UserRole.Admin)
                    {
                        await bot.SendMessage(callbackChatId, "❌ Access denied.");
                        break;
                    }

                    if (!int.TryParse(d.Replace("approve_deposit_", ""), out int requestId))
                    {
                        await bot.SendMessage(callbackChatId, "Invalid request ID.");
                        break;
                    }

                    var request = await _botDbContext.DepositRequests.Include(r => r.User)
                        .FirstOrDefaultAsync(r => r.Id == requestId);
                    if (request == null)
                    {
                        await bot.SendMessage(callbackChatId, "Request not found.");
                        break;
                    }

                    if (request.Status != "Pending")
                    {
                        await bot.SendMessage(callbackChatId, "Request already processed.");
                        break;
                    }

                    // Затверджуємо заявку
                    request.Status = "Approved";
                    request.User.Balance += request.Amount;
                    await _botDbContext.SaveChangesAsync();

                    await bot.SendMessage(callbackChatId, $"✅ Request #{request.Id} approved.");

                    // Повідомляємо користувача
                    var approveMessage = T(
                        GetUserLanguageCode(request.User),
                        $"✅ Your deposit request for {request.Amount} USDT has been approved. Your balance was updated.",
                        $"✅ Aapka {request.Amount} USDT deposit request approve ho gaya. Aapka balance update kar diya gaya.",
                        $"✅ Ваша заявка на депозит {request.Amount} USDT одобрена. Ваш баланс обновлен.",
                        $"✅ درخواست واریز {request.Amount} USDT شما تایید شد. موجودی شما به روز شد.",
                        $"✅ تمت الموافقة على طلب الإيداع الخاص بك بمبلغ {request.Amount} USDT. تم تحديث رصيدك.",
                        $"✅ 您的 {request.Amount} USDT 充值申请已通过，余额已更新。"
                    );

                    await bot.SendMessage(request.User.TelegramId, approveMessage);

                    await ShowMainMenuAsync(request.User.TelegramId);
                }
                    break;

                // Reject
                case var d when d.StartsWith("reject_deposit_"):
                {
                    var adminUser = await _manageService.GetUserByTelegramIdAsync(callbackChatId);
                    if (adminUser?.Role != UserRole.Admin)
                    {
                        await bot.SendMessage(callbackChatId, "❌ Access denied.");
                        break;
                    }

                    if (!int.TryParse(d.Replace("reject_deposit_", ""), out int requestId))
                    {
                        await bot.SendMessage(callbackChatId, "Invalid request ID.");
                        break;
                    }

                    var request = await _botDbContext.DepositRequests.Include(r => r.User)
                        .FirstOrDefaultAsync(r => r.Id == requestId);
                    if (request == null)
                    {
                        await bot.SendMessage(callbackChatId, "Request not found.");
                        break;
                    }

                    if (request.Status != "Pending")
                    {
                        await bot.SendMessage(callbackChatId, "Request already processed.");
                        break;
                    }

                    request.Status = "Rejected";
                    await _botDbContext.SaveChangesAsync();

                    await bot.SendMessage(callbackChatId, $"❌ Request #{request.Id} rejected.");

                    // Повідомляємо користувача
                    await bot.SendMessage(request.User.TelegramId,
                        $"❌ Your deposit request for {request.Amount} USDT has been rejected. Please contact support for details.");

                    await SendMessageWithBackButton(callbackChatId, "Back to Menu");
                    break;
                }
                case "withdraw":
                {
                    var withdrawUser = await _manageService.GetUserByTelegramIdAsync(callbackChatId);
                    var callbackLang = GetUserLanguageCode(withdrawUser);

                    if (withdrawUser == null || !withdrawUser.IsAuthorized)
                    {
                        string authMessage = T(
                            callbackLang,
                            "🔐 <b>Registration Required</b>\n\nTo make a withdrawal, please complete your registration first! 🚀",
                            "🔐 <b>Registration Zaroori Hai</b>\n\nWithdrawal karne ke liye, pehle apna registration poora karein! 🚀",
                            "🔐 <b>Требуется регистрация</b>\n\nЧтобы вывести средства, пожалуйста, сначала завершите регистрацию! 🚀",
                            "🔐 <b>ثبت نام لازم است</b>\n\nبرای برداشت، لطفاً ابتدا ثبت نام خود را کامل کنید! 🚀",
                            "🔐 <b>التسجيل مطلوب</b>\n\nلإجراء سحب، يرجى أولاً إكمال التسجيل! 🚀",
                            "🔐 <b>需要完成注册</b>\n\n如需提现，请先完成注册！🚀");

                        await _botClient.SendMessage(
                            chatId: callbackChatId,
                            text: authMessage,
                            parseMode: ParseMode.Html
                        );
                        await bot.AnswerCallbackQuery(callback.Id);
                        return;
                    }

                    // Перевірка мінімального балансу
                    decimal minWithdraw = 1.00m;
                    if (withdrawUser.Balance < minWithdraw)
                    {
                        string balanceMessage = T(
                            callbackLang,
                            $"⚠️ <b>Insufficient Balance</b>\n\n💰 <b>Current Balance:</b> {withdrawUser.Balance:N2} USDT\n📉 <b>Minimum Withdrawal:</b> {minWithdraw} USDT\n\nPlease deposit more funds to make a withdrawal. 💳",
                            $"⚠️ <b>Paryapt Balance Nahi</b>\n\n💰 <b>Current Balance:</b> {withdrawUser.Balance:N2} USDT\n📉 <b>Minimum Withdrawal:</b> {minWithdraw} USDT\n\nWithdrawal karne ke liye, kripya aur funds deposit karein. 💳",
                            $"⚠️ <b>Недостаточно средств</b>\n\n💰 <b>Текущий баланс:</b> {withdrawUser.Balance:N2} USDT\n📉 <b>Минимальный вывод:</b> {minWithdraw} USDT\n\nПожалуйста, пополните баланс для вывода средств. 💳",
                            $"⚠️ <b>موجودی کافی نیست</b>\n\n💰 <b>موجودی فعلی:</b> {withdrawUser.Balance:N2} USDT\n📉 <b>حداقل برداشت:</b> {minWithdraw} USDT\n\nبرای برداشت، لطفاً موجودی خود را افزایش دهید. 💳",
                            $"⚠️ <b>الرصيد غير كافٍ</b>\n\n💰 <b>الرصيد الحالي:</b> {withdrawUser.Balance:N2} USDT\n📉 <b>الحد الأدنى للسحب:</b> {minWithdraw} USDT\n\nيرجى إيداع المزيد من الأموال لإجراء السحب. 💳",
                            $"⚠️ <b>余额不足</b>\n\n💰 <b>当前余额：</b>{withdrawUser.Balance:N2} USDT\n📉 <b>最低提现金额：</b>{minWithdraw} USDT\n\n请先充值更多资金后再提现。💳");

                        await _botClient.SendMessage(
                            chatId: callbackChatId,
                            text: balanceMessage,
                            parseMode: ParseMode.Html
                        );
                        await bot.AnswerCallbackQuery(callback.Id);
                        return;
                    }

                    await _userStateService.SetStateAsync(callbackChatId, UserAction.WaitingWithdrawAmount);

                                        string messageText = T(
                                                callbackLang,
                                                $"💳 <b>Withdraw Funds</b>\n\n✨ <i>Ready to transfer your earnings?</i>\n\n💰 <b>Available Balance:</b> {withdrawUser.Balance:N2} USDT\n📉 <b>Minimum Withdrawal:</b> {minWithdraw} USDT\n\n🌐 <b>Network:</b> TRC20 (TRON)\n💵 <b>Currency:</b> USDT only\n⏰ <b>Processing Time:</b> Up to 12 hours\n\n↳ <b>Please enter the amount you want to withdraw:</b>",
                                                $"💳 <b>Funds Nikale</b>\n\n✨ <i>Apni earnings transfer karne ke liye taiyar?</i>\n\n💰 <b>Upalabdh Balance:</b> {withdrawUser.Balance:N2} USDT\n📉 <b>Minimum Withdrawal:</b> {minWithdraw} USDT\n\n🌐 <b>Network:</b> TRC20 (TRON)\n💵 <b>Currency:</b> Sirf USDT\n⏰ <b>Processing Time:</b> 12 ghante tak\n\n↳ <b>Kripya woh raash daalen jise aap withdraw karna chahte hain:</b>",
                                                $"💳 <b>Вывод средств</b>\n\n✨ <i>Готовы вывести заработанные средства?</i>\n\n💰 <b>Доступный баланс:</b> {withdrawUser.Balance:N2} USDT\n📉 <b>Минимальный вывод:</b> {minWithdraw} USDT\n\n🌐 <b>Сеть:</b> TRC20 (TRON)\n💵 <b>Валюта:</b> только USDT\n⏰ <b>Время обработки:</b> до 12 часов\n\n↳ <b>Введите сумму для вывода:</b>",
                                                $"💳 <b>برداشت وجه</b>\n\n✨ <i>برای انتقال سود خود آماده هستید؟</i>\n\n💰 <b>موجودی در دسترس:</b> {withdrawUser.Balance:N2} USDT\n📉 <b>حداقل برداشت:</b> {minWithdraw} USDT\n\n🌐 <b>شبکه:</b> TRC20 (TRON)\n💵 <b>ارز:</b> فقط USDT\n⏰ <b>زمان پردازش:</b> تا 12 ساعت\n\n↳ <b>لطفاً مبلغی را که می خواهید برداشت کنید وارد کنید:</b>",
                                                $"💳 <b>سحب الأموال</b>\n\n✨ <i>هل أنت مستعد لتحويل أرباحك؟</i>\n\n💰 <b>الرصيد المتاح:</b> {withdrawUser.Balance:N2} USDT\n📉 <b>الحد الأدنى للسحب:</b> {minWithdraw} USDT\n\n🌐 <b>الشبكة:</b> TRC20 (TRON)\n💵 <b>العملة:</b> USDT فقط\n⏰ <b>وقت المعالجة:</b> حتى 12 ساعة\n\n↳ <b>يرجى إدخال المبلغ الذي تريد سحبه:</b>",
                                                $"💳 <b>提现资金</b>\n\n✨ <i>准备转出您的收益了吗？</i>\n\n💰 <b>可用余额：</b>{withdrawUser.Balance:N2} USDT\n📉 <b>最低提现金额：</b>{minWithdraw} USDT\n\n🌐 <b>网络：</b>TRC20 (TRON)\n💵 <b>币种：</b>仅限 USDT\n⏰ <b>处理时间：</b>最长 12 小时\n\n↳ <b>请输入您想提现的金额：</b>");

                    // Створюємо клавіатуру з кнопкою Cancel
                    var cancelKeyboard = new InlineKeyboardMarkup(new[]
                    {
                        new[]
                        {
                            InlineKeyboardButton.WithCallbackData(
                                T(callbackLang, "❌ Cancel", "❌ Cancel", "❌ Отмена", "❌ لغو", "❌ إلغاء", "❌ 取消"),
                                "back_to_menu")
                        }
                    });

                    await EditOrSendAsync(
                        callbackChatId,
                        callbackMessageId,
                        messageText,
                        parseMode: ParseMode.Html,
                        replyMarkup: cancelKeyboard
                    );

                    await bot.AnswerCallbackQuery(callback.Id);
                    break;
                }

                case "settings":
                    var settingUser = await _manageService.GetUserByTelegramIdAsync(callbackChatId);

                    await _settingsService.ShowSettingsMenuAsync(callbackChatId, settingUser?.PreferredLanguage ?? settingUser?.Language ?? BotLanguageCodes.English, callbackMessageId);
                    break;


                case "change_language":
                    var changeLangUser = await _manageService.GetUserByTelegramIdAsync(callbackChatId);

                    await _settingsService.ShowLanguageSelectionAsync(callbackChatId, changeLangUser?.PreferredLanguage ?? changeLangUser?.Language ?? BotLanguageCodes.English, callbackMessageId);
                    break;

                case "change_login":
                    var loginUser = await _manageService.GetUserByTelegramIdAsync(callbackChatId);
                    await _settingsService.AskForNewLoginAsync(callbackChatId, loginUser?.PreferredLanguage ?? loginUser?.Language ?? BotLanguageCodes.English);
                    // Встановлюємо стан очікування нового логіну
                    await _userStateService.SetStateAsync(callbackChatId, UserAction.WaitingForNewLogin);
                    break;

                case "change_password":
                    var passwordUser = await _manageService.GetUserByTelegramIdAsync(callbackChatId);
                    await _settingsService.AskForNewPasswordAsync(callbackChatId, passwordUser?.PreferredLanguage ?? passwordUser?.Language ?? BotLanguageCodes.English);
                    // Встановлюємо стан очікування нового пароля
                    await _userStateService.SetStateAsync(callbackChatId, UserAction.WaitingForNewPassword);
                    break;

                case "back_to_settings":
                    var backToSettingsUser = await _manageService.GetUserByTelegramIdAsync(callbackChatId);
                    await _settingsService.ShowSettingsMenuAsync(callbackChatId, backToSettingsUser?.PreferredLanguage ?? backToSettingsUser?.Language ?? BotLanguageCodes.English, callbackMessageId);
                    break;
                case "change_wallet":
                    try
                    {
                        // Отримуємо дані користувача
                        var walletUser = await _manageService.GetUserByTelegramIdAsync(callbackChatId);
                        if (walletUser == null)
                        {
                            _logger.LogWarning("User not found for chat ID: {ChatId}", callbackChatId);
                            await _botClient.AnswerCallbackQuery(callback.Id, "❌ User not found");
                            return;
                        }

                        // Відправляємо запит на нову адресу гаманця
                        var walletLang = GetUserLanguageCode(walletUser);
                        await _settingsService.AskForNewWalletAddressAsync(callbackChatId, walletLang);

                        // Встановлюємо стан очікування введення даних
                        await _userStateService.SetStateAsync(callbackChatId, UserAction.WaitingForNewWalletAddress);

                        // Підтверджуємо обробку запиту
                        await _botClient.AnswerCallbackQuery(
                            callback.Id,
                            T(walletLang, "📍 Enter your new wallet address", "📍 Apna naya wallet address enter karen", "📍 Введите новый адрес кошелька", "📍 آدرس جدید کیف پول خود را وارد کنید", "📍 أدخل عنوان المحفظة الجديد", "📍 请输入新的钱包地址")
                        );

                        _logger.LogInformation("Wallet address change requested by user {ChatId}", callbackChatId);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error processing wallet address change for chat ID: {ChatId}",
                            callbackChatId);
                        await _botClient.AnswerCallbackQuery(
                            callback.Id,
                            "❌ Service temporarily unavailable"
                        );
                    }

                    break;

                case "invest":
                {
                    var investUser = await _manageService.GetUserByTelegramIdAsync(callbackChatId);
                    var investLang = GetUserLanguageCode(investUser);

                    // Перевірка авторизації
                    if (investUser == null || !investUser.IsAuthorized)
                    {
                        string authMessage = T(
                            investLang,
                            "🔐 <b>Registration Required</b>\n\nTo start investing, please complete your registration first! 🚀",
                            "🔐 <b>Registration Zaroori Hai</b>\n\nInvest shuru karne ke liye, pehle apna registration poora karein! 🚀",
                            "🔐 <b>Требуется регистрация</b>\n\nЧтобы начать инвестировать, пожалуйста, сначала завершите регистрацию! 🚀",
                            "🔐 <b>ثبت نام لازم است</b>\n\nبرای شروع سرمایه گذاری، لطفاً ابتدا ثبت نام خود را کامل کنید! 🚀",
                            "🔐 <b>التسجيل مطلوب</b>\n\nلبدء الاستثمار، يرجى إكمال التسجيل أولاً! 🚀",
                            "🔐 <b>需要完成注册</b>\n\n如需开始投资，请先完成注册！🚀");

                        await _botClient.SendMessage(
                            chatId: callbackChatId,
                            text: authMessage,
                            parseMode: ParseMode.Html
                        );
                        return;
                    }

                    await _userStateService.SetStateAsync(callbackChatId, UserAction.WaitingInvestDuration);

                    var durationKeyboard = _localizationService.NormalizeCode(investLang) == BotLanguageCodes.English
                        ? new InlineKeyboardMarkup(new[]
                        {
                            new[]
                            {
                                InlineKeyboardButton.WithCallbackData("📆 2 Days | 1%-3% ROI", "duration_48"),
                                InlineKeyboardButton.WithCallbackData("📆 1 Week | 8%-13% ROI", "duration_1w")
                            },
                            new[]
                            {
                                InlineKeyboardButton.WithCallbackData("📆 2 Weeks | 22%-30% ROI", "duration_2w")
                            },
                            new[]
                            {
                                InlineKeyboardButton.WithCallbackData("❌ Cancel", "back_to_menu")
                            }
                        })
                        : new InlineKeyboardMarkup(new[]
                        {
                            new[]
                            {
                                InlineKeyboardButton.WithCallbackData("📆 2 Din | 1%-3% Faayda", "duration_48"),
                                InlineKeyboardButton.WithCallbackData("📆 1 Hafta | 8%-13% Faayda", "duration_1w")
                            },
                            new[]
                            {
                                InlineKeyboardButton.WithCallbackData("📆 2 Hafte | 22%-30% Faayda", "duration_2w")
                            },
                            new[]
                            {
                                InlineKeyboardButton.WithCallbackData("❌ Cancel", "back_to_menu")
                            }
                        });

                    var messageText = T(
                        investLang,
                        "💰 <b>Choose Investment Plan</b>\n\n✨ <i>Select the duration that suits your goals:</i>\n\n• <b>Short-term</b> - Quick returns\n• <b>Medium-term</b> - Balanced growth  \n• <b>Long-term</b> - Maximum profit\n\n⏳ <b>Please select your preferred duration:</b>",
                        "💰 <b>Investment Plan Chuno</b>\n\n✨ <i>Apne targets ke hisaab se time chuno:</i>\n\n• <b>Short-term</b> - Jaldi returns\n• <b>Medium-term</b> - Balanced growth  \n• <b>Long-term</b> - Zyada profit\n\n⏳ <b>Apna preferred duration chuno:</b>",
                        "💰 <b>Выберите инвестиционный план</b>\n\n✨ <i>Выберите срок, который подходит вашим целям:</i>\n\n• <b>Краткосрочный</b> - быстрый результат\n• <b>Среднесрочный</b> - сбалансированный рост\n• <b>Долгосрочный</b> - максимальная прибыль\n\n⏳ <b>Выберите предпочитаемый срок:</b>",
                        "💰 <b>طرح سرمایه گذاری را انتخاب کنید</b>\n\n✨ <i>مدتی را انتخاب کنید که با اهداف شما سازگار است:</i>\n\n• <b>کوتاه مدت</b> - بازده سریع\n• <b>میان مدت</b> - رشد متعادل\n• <b>بلندمدت</b> - حداکثر سود\n\n⏳ <b>لطفاً مدت مورد نظر خود را انتخاب کنید:</b>",
                        "💰 <b>اختر خطة الاستثمار</b>\n\n✨ <i>اختر المدة التي تناسب أهدافك:</i>\n\n• <b>قصير المدى</b> - عوائد سريعة\n• <b>متوسط المدى</b> - نمو متوازن\n• <b>طويل المدى</b> - أقصى ربح\n\n⏳ <b>يرجى اختيار المدة المفضلة:</b>",
                        "💰 <b>选择投资计划</b>\n\n✨ <i>请选择适合您目标的期限：</i>\n\n• <b>短期</b> - 快速回报\n• <b>中期</b> - 均衡增长\n• <b>长期</b> - 最大收益\n\n⏳ <b>请选择您偏好的期限：</b>");

                    await EditOrSendAsync(
                        callbackChatId,
                        callbackMessageId,
                        messageText,
                        parseMode: ParseMode.Html,
                        replyMarkup: durationKeyboard
                    );
                    break;
                }

                case "duration_48":
                    _userDurations[callbackChatId] = InvestmentDuration.TwoDays;
                    await _userStateService.SetStateAsync(callbackChatId, UserAction.WaitingInvestAmount);
                    var durationFortyUser = await _manageService.GetUserByTelegramIdAsync(callbackChatId);
                    var durFourLang = GetUserLanguageCode(durationFortyUser);

                    string messageFortyDur = T(durFourLang,
                        "💎 <b>2-Day Investment</b>\n\n📈 <i>Expected ROI: 1% - 3%</i>\n\n💸 <b>Enter investment amount:</b>\n\n• Minimum: 1 USDT\n• Maximum: No limit\n\n↳ Please type the amount:",
                        "💎 <b>2-Din Investment</b>\n\n📈 <i>Expected ROI: 1% - 3%</i>\n\n💸 <b>Investment raash daalo:</b>\n\n• Minimum: 1 USDT\n• Maximum: No limit\n\n↳ Kripya raash type karein:",
                        "💎 <b>Инвестиция на 2 дня</b>\n\n📈 <i>Ожидаемый ROI: 1% - 3%</i>\n\n💸 <b>Введите сумму инвестиции:</b>\n\n• Минимум: 1 USDT\n• Максимум: без ограничений\n\n↳ Введите сумму:",
                        "💎 <b>سرمایه گذاری 2 روزه</b>\n\n📈 <i>ROI مورد انتظار: 1% - 3%</i>\n\n💸 <b>مبلغ سرمایه گذاری را وارد کنید:</b>\n\n• حداقل: 1 USDT\n• حداکثر: بدون محدودیت\n\n↳ لطفاً مبلغ را وارد کنید:",
                        "💎 <b>استثمار لمدة يومين</b>\n\n📈 <i>العائد المتوقع: 1% - 3%</i>\n\n💸 <b>أدخل مبلغ الاستثمار:</b>\n\n• الحد الأدنى: 1 USDT\n• الحد الأقصى: بدون حد\n\n↳ يرجى إدخال المبلغ:",
                        "💎 <b>2 天投资</b>\n\n📈 <i>预期收益率：1% - 3%</i>\n\n💸 <b>请输入投资金额：</b>\n\n• 最低：1 USDT\n• 最高：无上限\n\n↳ 请输入金额：");

                    await EditOrSendAsync(
                        callbackChatId,
                        callbackMessageId,
                        messageFortyDur,
                        parseMode: ParseMode.Html
                    );
                    break;

                case "duration_1w":
                    _userDurations[callbackChatId] = InvestmentDuration.OneWeek;
                    await _userStateService.SetStateAsync(callbackChatId, UserAction.WaitingInvestAmount);
                    var durationOneUser = await _manageService.GetUserByTelegramIdAsync(callbackChatId);
                    var durLang = GetUserLanguageCode(durationOneUser);

                    string messageDur = T(durLang,
                        "💎 <b>1-Week Investment</b>\n\n📈 <i>Expected ROI: 8% - 13%</i>\n\n💸 <b>Enter investment amount:</b>\n\n• Minimum: 1 USDT\n• Maximum: No limit\n\n↳ Please type the amount:",
                        "💎 <b>1-Hafta Investment</b>\n\n📈 <i>Expected ROI: 8% - 13%</i>\n\n💸 <b>Investment raash daalo:</b>\n\n• Minimum: 1 USDT\n• Maximum: No limit\n\n↳ Kripya raash type karein:",
                        "💎 <b>Инвестиция на 1 неделю</b>\n\n📈 <i>Ожидаемый ROI: 8% - 13%</i>\n\n💸 <b>Введите сумму инвестиции:</b>\n\n• Минимум: 1 USDT\n• Максимум: без ограничений\n\n↳ Введите сумму:",
                        "💎 <b>سرمایه گذاری 1 هفته ای</b>\n\n📈 <i>ROI مورد انتظار: 8% - 13%</i>\n\n💸 <b>مبلغ سرمایه گذاری را وارد کنید:</b>\n\n• حداقل: 1 USDT\n• حداکثر: بدون محدودیت\n\n↳ لطفاً مبلغ را وارد کنید:",
                        "💎 <b>استثمار لمدة أسبوع</b>\n\n📈 <i>العائد المتوقع: 8% - 13%</i>\n\n💸 <b>أدخل مبلغ الاستثمار:</b>\n\n• الحد الأدنى: 1 USDT\n• الحد الأقصى: بدون حد\n\n↳ يرجى إدخال المبلغ:",
                        "💎 <b>1 周投资</b>\n\n📈 <i>预期收益率：8% - 13%</i>\n\n💸 <b>请输入投资金额：</b>\n\n• 最低：1 USDT\n• 最高：无上限\n\n↳ 请输入金额：");

                    await EditOrSendAsync(
                        callbackChatId,
                        callbackMessageId,
                        messageDur,
                        parseMode: ParseMode.Html
                    );
                    break;

                case "duration_2w":
                    _userDurations[callbackChatId] = InvestmentDuration.TwoWeeks;
                    await _userStateService.SetStateAsync(callbackChatId, UserAction.WaitingInvestAmount);
                    var durationTwoUser = await _manageService.GetUserByTelegramIdAsync(callbackChatId);
                    var durTwoLang = GetUserLanguageCode(durationTwoUser);

                    string messageDurTwo = T(durTwoLang,
                        "💎 <b>2-Week Investment</b>\n\n📈 <i>Expected ROI: 22% - 30%</i>\n\n💸 <b>Enter investment amount:</b>\n\n• Minimum: 1 USDT\n• Maximum: No limit\n\n↳ Please type the amount:",
                        "💎 <b>2-Hafte Investment</b>\n\n📈 <i>Expected ROI: 22% - 30%</i>\n\n💸 <b>Investment raash daalo:</b>\n\n• Minimum: 1 USDT\n• Maximum: No limit\n\n↳ Kripya raash type karein:",
                        "💎 <b>Инвестиция на 2 недели</b>\n\n📈 <i>Ожидаемый ROI: 22% - 30%</i>\n\n💸 <b>Введите сумму инвестиции:</b>\n\n• Минимум: 1 USDT\n• Максимум: без ограничений\n\n↳ Введите сумму:",
                        "💎 <b>سرمایه گذاری 2 هفته ای</b>\n\n📈 <i>ROI مورد انتظار: 22% - 30%</i>\n\n💸 <b>مبلغ سرمایه گذاری را وارد کنید:</b>\n\n• حداقل: 1 USDT\n• حداکثر: بدون محدودیت\n\n↳ لطفاً مبلغ را وارد کنید:",
                        "💎 <b>استثمار لمدة أسبوعين</b>\n\n📈 <i>العائد المتوقع: 22% - 30%</i>\n\n💸 <b>أدخل مبلغ الاستثمار:</b>\n\n• الحد الأدنى: 1 USDT\n• الحد الأقصى: بدون حد\n\n↳ يرجى إدخال المبلغ:",
                        "💎 <b>2 周投资</b>\n\n📈 <i>预期收益率：22% - 30%</i>\n\n💸 <b>请输入投资金额：</b>\n\n• 最低：1 USDT\n• 最高：无上限\n\n↳ 请输入金额：");

                    await EditOrSendAsync(
                        callbackChatId,
                        callbackMessageId,
                        messageDurTwo,
                        parseMode: ParseMode.Html
                    );
                    break;
                case "admin_withdrawals":
                {
                    // Показуємо список очікуючих заявок на вивід
                    await _withdrawalAdminService.ShowPendingWithdrawalsAsync(callbackChatId);
                    break;
                }

                // Callback для затвердження заявки
                case var d when d.StartsWith("approve_withdraw_"):
                {
                    if (int.TryParse(d.Replace("approve_withdraw_", ""), out var approveId))
                    {
                        var userChatId =
                            await _withdrawalAdminService.ApproveWithdrawalAsync(callbackChatId, approveId);

                        // Якщо знайшли користувача — показуємо йому головне меню
                        if (userChatId.HasValue)
                        {
                            await ShowMainMenuAsync(userChatId.Value);
                        }
                    }
                    else
                    {
                        await bot.SendMessage(callbackChatId, "❌ Invalid request ID.");
                    }

                    break;
                }
                // Callback для відхилення заявки
                case var d when d.StartsWith("reject_withdraw_"):
                {
                    if (int.TryParse(d.Replace("reject_withdraw_", ""), out var rejectId))
                        await _withdrawalAdminService.RejectWithdrawalAsync(callbackChatId, rejectId);
                    else
                        await bot.SendMessage(callbackChatId, "❌ Invalid request ID.");
                    break;
                }
                case "admin_toggle_bonus":
                {
                    var admin = await _manageService.GetUserByTelegramIdAsync(callbackChatId);
                    if (admin?.Role != UserRole.Admin) return;

                    // Створюємо меню вибору дії
                    var actionButtons = new List<InlineKeyboardButton[]>
                    {
                        new[]
                        {
                            InlineKeyboardButton.WithCallbackData("✅ Activate 25% Bonus", "admin_activate_bonus"),
                            InlineKeyboardButton.WithCallbackData("❌ Deactivate 25% Bonus", "admin_deactivate_bonus")
                        },
                        new[]
                        {
                            InlineKeyboardButton.WithCallbackData("↩️ Back to Admin Panel", "admin_panel_back")
                        }
                    };

                    // Редагуємо поточне повідомлення або відправляємо нове
                    try
                    {
                        await _botClient.EditMessageText(
                            chatId: callbackChatId,
                            messageId: callbackMessageId,
                            text: "🎯 <b>25% Bonus Management</b>\n\nChoose action:",
                            parseMode: ParseMode.Html,
                            replyMarkup: new InlineKeyboardMarkup(actionButtons)
                        );
                    }
                    catch
                    {
                        // Якщо не вдалося редагувати, відправляємо нове
                        await _botClient.SendMessage(
                            chatId: callbackChatId,
                            text: "🎯 <b>25% Bonus Management</b>\n\nChoose action:",
                            parseMode: ParseMode.Html,
                            replyMarkup: new InlineKeyboardMarkup(actionButtons)
                        );
                    }

                    break;
                }

                case "admin_activate_bonus":
                {
                    var admin = await _manageService.GetUserByTelegramIdAsync(callbackChatId);
                    if (admin?.Role != UserRole.Admin) return;

                    // Встановлюємо спеціальний стан для активації
                    await _userStateService.SetStateAsync(callbackChatId, UserAction.WaitingBonusActivate);

                    await _botClient.SendMessage(
                        chatId: callbackChatId,
                        text: "👤 <b>Activate 25% Bonus</b>\n\nEnter Telegram ID of the user:",
                        parseMode: ParseMode.Html
                    );
                    break;
                }

                case "admin_deactivate_bonus":
                {
                    var admin = await _manageService.GetUserByTelegramIdAsync(callbackChatId);
                    if (admin?.Role != UserRole.Admin) return;

                    // Встановлюємо спеціальний стан для деактивації
                    await _userStateService.SetStateAsync(callbackChatId, UserAction.WaitingBonusDeactivate);

                    await _botClient.SendMessage(
                        chatId: callbackChatId,
                        text: "👤 <b>Deactivate 25% Bonus</b>\n\nEnter Telegram ID of the user:",
                        parseMode: ParseMode.Html
                    );
                    break;
                }
                case "quests":
                    try
                    {
                        await HandleQuestCallback(callback);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error in /quests: {ex.Message}");
                    }

                    break;

                case "update_quests_progress":
                    try
                    {
                        await _questService.UpdateUserProgressAutomaticallyAsync(callback.From.Id);

                        var userProgress = await _manageService.GetUserByTelegramIdAsync(callback.From.Id);
                        var langProgress = GetUserLanguageCode(userProgress);

                        await _botClient.AnswerCallbackQuery(callback.Id,
                            T(langProgress, "🔄 Progress updated automatically!", "🔄 Progress automatically update ho gaya!", "🔄 Прогресс обновлен автоматически!", "🔄 پیشرفت به صورت خودکار به روز شد!", "🔄 تم تحديث التقدم تلقائياً!", "🔄 进度已自动更新！"),
                            showAlert: false);

                        // Оновлюємо повідомлення з квестами
                        await HandleQuestCallback(callback);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error in update_quests_progress: {ex.Message}");
                    }

                    break;

                case var dataQuest when data.StartsWith("complete_quest_"):
                    try
                    {
                        var parts = callback.Data.Split('_');
                        if (parts.Length >= 3 && int.TryParse(parts[2], out int questId))
                        {
                            var success = await _questService.UpdateQuestProgressAsync(callback.From.Id, questId, 1);

                            var userDataQuest = await _manageService.GetUserByTelegramIdAsync(callback.From.Id);
                            var langData = GetUserLanguageCode(userDataQuest);

                            if (success)
                            {
                                await _botClient.AnswerCallbackQuery(callback.Id,
                                    T(langData, "✅ Progress updated!", "✅ Progress update ho gaya!", "✅ Прогресс обновлен!", "✅ پیشرفت به روز شد!", "✅ تم تحديث التقدم!", "✅ 进度已更新！"),
                                    showAlert: false);

                                // Оновлюємо повідомлення з квестами
                                await HandleQuestCallback(callback);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error in complete_quest: {ex.Message}");
                    }

                    break;

                case var dataQuest2 when data.StartsWith("claim_quest_"):
                    try
                    {
                        var parts = callback.Data.Split('_');
                        if (parts.Length >= 3 && int.TryParse(parts[2], out int questId))
                        {
                            var (success, reward) =
                                await _questService.ClaimQuestRewardAsync(callback.From.Id, questId);

                            var userClaim = await _manageService.GetUserByTelegramIdAsync(callback.From.Id);
                            var langClaim = GetUserLanguageCode(userClaim);

                            if (success)
                            {
                                var rewardMsg = T(langClaim,
                                    $"🎉 Quest completed!\n\n💰 You received {reward} USDT",
                                    $"🎉 Quest complete ho gaya!\n\n💰 Aapko {reward} USDT mila",
                                    $"🎉 Квест выполнен!\n\n💰 Вы получили {reward} USDT",
                                    $"🎉 ماموریت تکمیل شد!\n\n💰 شما {reward} USDT دریافت کردید",
                                    $"🎉 تم إكمال المهمة!\n\n💰 لقد استلمت {reward} USDT",
                                    $"🎉 任务已完成！\n\n💰 您获得了 {reward} USDT");

                                await _botClient.AnswerCallbackQuery(callback.Id, rewardMsg, showAlert: true);

                                // Оновлюємо повідомлення після отримання нагороди
                                await HandleQuestCallback(callback);
                            }
                            else
                            {
                                var errorMsg = T(langClaim,
                                    "❌ Quest already claimed or not completed",
                                    "❌ Quest reward pehle hi mil chuka hai ya quest complete nahi hua",
                                    "❌ Награда уже получена или квест не выполнен",
                                    "❌ پاداش قبلاً دریافت شده یا ماموریت کامل نشده است",
                                    "❌ تمت المطالبة بالمكافأة بالفعل أو لم تكتمل المهمة",
                                    "❌ 奖励已领取或任务尚未完成");

                                await _botClient.AnswerCallbackQuery(callback.Id, errorMsg, showAlert: true);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error in claim_quest: {ex.Message}");
                        await _botClient.AnswerCallbackQuery(callback.Id);
                    }

                    break;


                case "admin_toggle_maintenance":
                {
                    var admin = await _manageService.GetUserByTelegramIdAsync(callbackChatId);
                    if (admin?.Role != UserRole.Admin) return;

                    var actionButtons = new List<InlineKeyboardButton[]>
                    {
                        new[]
                        {
                            InlineKeyboardButton.WithCallbackData("✅ Enable maintenance",
                                "admin_enable_maintenance"),
                            InlineKeyboardButton.WithCallbackData("❌ Disable maintenance",
                                "admin_disable_maintenance")
                        },
                        new[]
                        {
                            InlineKeyboardButton.WithCallbackData("↩️ Back to Admin Panel", "admin_panel_back")
                        }
                    };

                    try
                    {
                        await _botClient.EditMessageText(
                            chatId: callbackChatId,
                            messageId: callbackMessageId,
                            text: "🛠 <b>Maintenance Messages</b>\n\nChoose action:",
                            parseMode: ParseMode.Html,
                            replyMarkup: new InlineKeyboardMarkup(actionButtons)
                        );
                    }
                    catch
                    {
                        await _botClient.SendMessage(
                            chatId: callbackChatId,
                            text: "🛠 <b>Maintenance Messages</b>\n\nChoose action:",
                            parseMode: ParseMode.Html,
                            replyMarkup: new InlineKeyboardMarkup(actionButtons)
                        );
                    }

                    break;
                }

                case "admin_enable_maintenance":
                {
                    var admin = await _manageService.GetUserByTelegramIdAsync(callbackChatId);
                    if (admin?.Role != UserRole.Admin) return;

                    // Просто вмикаємо техобслуговування з фіксованим текстом
                    await _maintenanceService.NotifyMaintenanceStartAsync("Scheduled maintenance");
                    await _botClient.AnswerCallbackQuery(callback.Id, "✅ Maintenance enabled");

                    break;
                }

                case "admin_disable_maintenance":
                {
                    var admin = await _manageService.GetUserByTelegramIdAsync(callbackChatId);
                    if (admin?.Role != UserRole.Admin) return;

                    // Вимікаємо техобслуговування
                    await _maintenanceService.NotifyMaintenanceEndAsync();
                    await _botClient.AnswerCallbackQuery(callback.Id, "❌ Maintenance disabled");

                    // Затримка перед показом меню
                    await Task.Delay(1000);

                    // Показуємо меню всім зареєстрованим користувачам
                    var users = await _botDbContext.Users
                        .AsNoTracking()
                        .Where(u => u.IsAuthorized)
                        .Select(u => new { u.TelegramId, u.Language })
                        .ToListAsync();

                    foreach (var userOne in users)
                    {
                        try
                        {
                            // Додаємо більшу затримку між повідомленнями
                            await ShowMainMenuAsync(userOne.TelegramId);
                            await Task.Delay(500); // Збільшена затримка
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Error showing menu to {userOne.TelegramId}: {ex.Message}");
                            // Продовжуємо для інших користувачів
                        }
                    }

                    break;
                }
                case "admin_delay_compensation":
                {
                    var admin = await _manageService.GetUserByTelegramIdAsync(callbackChatId);
                    if (admin?.Role != UserRole.Admin) return;

                    // Спочатку показуємо скільки користувачів отримають повідомлення
                    var count = await _delayCompensationService.GetAffectedUsersCountAsync();

                    var keyboard = new InlineKeyboardMarkup(new[]
                    {
                        new[]
                        {
                            InlineKeyboardButton.WithCallbackData("✅ Confirm", "admin_delay_confirm"),
                            InlineKeyboardButton.WithCallbackData("❌ Cancel", "admin_cancel")
                        }
                    });

                    await _botClient.SendMessage(
                        callbackChatId,
                        $"📊 <b>Delay Compensation Preview</b>\n\n" +
                        $"Total users to notify: <b>{count}</b>\n" +
                        $"Each user will receive: <b>3 USDT</b>\n\n" +
                        $"Message will be sent about 5-day delay due to market conditions.\n\n" +
                        $"Do you want to proceed?",
                        parseMode: ParseMode.Html,
                        replyMarkup: keyboard);

                    await _botClient.AnswerCallbackQuery(callback.Id);
                    break;
                }

                case "admin_delay_confirm":
                {
                    var admin = await _manageService.GetUserByTelegramIdAsync(callbackChatId);
                    if (admin?.Role != UserRole.Admin) return;

                    await _botClient.AnswerCallbackQuery(callback.Id, "⏳ Processing...");

                    // Виконуємо розсилку та нарахування
                    await _delayCompensationService.NotifyDelayAndCompensateAsync();

                    await _botClient.SendMessage(
                        callbackChatId,
                        "✅ <b>Delay notifications sent and bonuses added!</b>\n\n" +
                        "All authorized users have been notified and received 3 USDT bonus.",
                        parseMode: ParseMode.Html);

                    break;
                }
            }

            return;
        }


        //Text messages handler 


        if (update.Type != UpdateType.Message || update.Message?.Text == null)
            return;

        var message = update.Message;
        var chatId = message.Chat.Id;
        var msgText = message.Text;
        var username = message.From.Username ?? $"user{chatId}";

        var state = await _userStateService.GetStateAsync(chatId);


        // =================== 2️⃣ Перевірка користувача ===================
        string? referralCode = null;
        if (msgText.Contains(" ") && msgText.Split(' ').Length > 1)
        {
            var parts = msgText.Split(' ');
            referralCode = parts[1];
        }

        var user = await _manageService.FindOrCreateTempUserAsync(chatId, username, referralCode);
        var lang = GetUserLanguageCode(user);

        // ================= WaitingRulesAccept =================
        // =================== WaitingRulesAccept ===================
        if (state == UserAction.WaitingRulesAccept)
        {
            await bot.SendMessage(chatId,
                T(lang,
                    "❗ Please use the ✅ button to confirm that you have read the rules.",
                    "❗ Kripya rules ko confirm karne ke liye ✅ button dabayein.",
                    "❗ Пожалуйста, используйте кнопку ✅, чтобы подтвердить, что вы прочитали правила.",
                    "❗ لطفاً برای تایید مطالعه قوانین از دکمه ✅ استفاده کنید.",
                    "❗ يرجى استخدام زر ✅ لتأكيد أنك قرأت القواعد.",
                    "❗ 请使用 ✅ 按钮确认您已阅读规则。"));
            return;
        }

        // =================== Registration ===================

        // ------------------- Реєстрація -------------------

        if (state == UserAction.WaitingRegisterUsername)
        {
            _pendingUsernames[chatId] = msgText;
            await _userStateService.SetStateAsync(chatId, UserAction.WaitingRegisterPassword);

            await bot.SendMessage(chatId,
                T(lang,
                    "🔑 Enter your password (at least 6 characters, letters + numbers):",
                    "🔑 Password daalo (kam se kam 6 characters, letters + numbers):",
                    "🔑 Введите пароль (не менее 6 символов, буквы и цифры):",
                    "🔑 رمز عبور خود را وارد کنید (حداقل 6 کاراکتر، شامل حروف و اعداد):",
                    "🔑 أدخل كلمة المرور (6 أحرف على الأقل، أحرف وأرقام):",
                    "🔑 请输入密码（至少 6 个字符，包含字母和数字）："));
            return;
        }

        if (state == UserAction.WaitingRegisterPassword)
        {
            string regPassword = msgText.Trim();

            // 🔎 Перевірка довжини пароля
            if (regPassword.Length < 6)
            {
                await bot.SendMessage(chatId,
                    T(lang,
                        "❌ Password too short. Please enter at least 6 characters (letters + numbers):",
                        "❌ Password bahut chhota hai. Kam se kam 6 characters daalo (letters + numbers):",
                        "❌ Пароль слишком короткий. Введите не менее 6 символов (буквы и цифры):",
                        "❌ رمز عبور خیلی کوتاه است. لطفاً حداقل 6 کاراکتر وارد کنید (حروف و اعداد):",
                        "❌ كلمة المرور قصيرة جداً. يرجى إدخال 6 أحرف على الأقل (أحرف وأرقام):",
                        "❌ 密码太短。请输入至少 6 个字符（字母和数字）："));
                await _userStateService.SetStateAsync(chatId, UserAction.WaitingRegisterPassword);
                return;
            }

            _pendingPasswords[chatId] = regPassword;
            await _userStateService.SetStateAsync(chatId, UserAction.WaitingRegisterWallet);

            await bot.SendMessage(chatId,
                                T(lang,
                                        "💼 <b>Enter your TRC20 wallet address:</b>\n\nℹ️ <i>This is your wallet from which deposits and withdrawals will be processed.</i>\n⚠️ <i>Wallet address must be at least 32 characters long</i>",
                                        "💼 <b>Apna TRC20 wallet address daalo:</b>\n\nℹ️ <i>Ye aapka wallet hoga jisme se deposit aur withdrawal hoga.</i>\n⚠️ <i>Wallet address kam se kam 32 characters lamba hona chahiye</i>",
                                        "💼 <b>Введите адрес вашего TRC20-кошелька:</b>\n\nℹ️ <i>Это ваш кошелек, с которого будут обрабатываться пополнения и выводы.</i>\n⚠️ <i>Адрес кошелька должен содержать не менее 32 символов</i>",
                                        "💼 <b>آدرس کیف پول TRC20 خود را وارد کنید:</b>\n\nℹ️ <i>این همان کیف پولی است که واریز و برداشت از آن انجام می شود.</i>\n⚠️ <i>آدرس کیف پول باید حداقل 32 کاراکتر باشد</i>",
                                        "💼 <b>أدخل عنوان محفظة TRC20 الخاصة بك:</b>\n\nℹ️ <i>هذه هي المحفظة التي ستتم منها عمليات الإيداع والسحب.</i>\n⚠️ <i>يجب ألا يقل طول عنوان المحفظة عن 32 حرفاً</i>",
                                        "💼 <b>请输入您的 TRC20 钱包地址：</b>\n\nℹ️ <i>这是您用于充值和提现的钱包地址。</i>\n⚠️ <i>钱包地址长度必须至少为 32 个字符</i>"),
                parseMode: ParseMode.Html);
            return;
        }

        if (state == UserAction.WaitingRegisterWallet)
        {
            if (!_pendingUsernames.TryGetValue(chatId, out var regUsername) ||
                !_pendingPasswords.TryGetValue(chatId, out var regPassword))
                return;
            // Отримуємо користувача та його реферальний код
            var registeringUser = await _manageService.GetUserByTelegramIdAsync(chatId);
            var regReferralCode = registeringUser?.PendingReferralCode;

            string walletAddress = msgText.Trim();

            // 🔎 Перевірка довжини гаманця
            if (walletAddress.Length < 32)
            {
                await bot.SendMessage(chatId,
                                        T(lang,
                                                "❌ Wallet address too short. Please enter a valid USDT wallet (at least 32 characters):\n\nℹ️ <i>This is your wallet from which deposits and withdrawals will be processed.</i>",
                                                "❌ Wallet address bahut chhota hai. Sahi USDT wallet daalo (kam se kam 32 characters):\n\nℹ️ <i>Ye aapka wallet hoga jisme se deposit aur withdrawal hoga.</i>",
                                                "❌ Адрес кошелька слишком короткий. Введите корректный USDT-кошелек (не менее 32 символов):\n\nℹ️ <i>Это ваш кошелек для пополнения и вывода средств.</i>",
                                                "❌ آدرس کیف پول خیلی کوتاه است. لطفاً یک کیف پول معتبر USDT وارد کنید (حداقل 32 کاراکتر):\n\nℹ️ <i>این همان کیف پولی است که واریز و برداشت از آن انجام می شود.</i>",
                                                "❌ عنوان المحفظة قصير جداً. يرجى إدخال محفظة USDT صالحة (32 حرفاً على الأقل):\n\nℹ️ <i>هذه هي المحفظة التي ستتم منها عمليات الإيداع والسحب.</i>",
                                                "❌ 钱包地址过短。请输入有效的 USDT 钱包地址（至少 32 个字符）：\n\nℹ️ <i>这是您用于充值和提现的钱包地址。</i>"),
                    parseMode: ParseMode.Html);

                await _userStateService.SetStateAsync(chatId, UserAction.WaitingRegisterWallet);
                return;
            }

            var result =
                await _registerService.RegisterUserAsync(chatId, regUsername, regPassword, walletAddress,
                    GetUserLanguageCode(user), regReferralCode);

            if (registeringUser != null)
            {
                registeringUser.PendingReferralCode = null;
                await _botDbContext.SaveChangesAsync();
            }

            await bot.SendMessage(chatId, result.Message);

            // --- ПОЧАТОК: Нова інструкція після реєстрації ---
            if (result.Success) // Надсилаємо інструкцію тільки якщо реєстрація була успішною
            {
                                string instructions = T(
                                        lang,
                                        "🎉 <b>Registration Successful!</b>\n\n📖 <b>Quick Start Guide:</b>\n\n1️⃣ <b>Make a Deposit</b>\n • Enter the amount in USDT (TRC20) and confirm.\n\n2️⃣ <b>Wait for Confirmation</b>\n • Once the transaction is confirmed, your deposit will appear in your profile.\n\n3️⃣ <b>Track Your Balance</b>\n • In the My Profile section you will see your deposit, active investments, and profit.\n\n4️⃣ <b>Start Investing</b>\n • To activate AI trading, press the Invest button and choose the period.\n\n🔒 <b>Important</b>\n • All funds are securely linked to your wallet.\n • Withdrawals are possible only to the same wallet used for deposit.\n • As this is beta testing, small delays may occur.",
                                        "🎉 <b>Registration Safal Ho Gayi!</b>\n\n📖 <b>Jaldi Start Guide:</b>\n\n1️⃣ <b>Deposit Karo</b>\n • USDT (TRC20) mein raashi daalen aur confirm karen.\n\n2️⃣ <b>Confirmation Ka Intezar Karo</b>\n • Transaction confirm hone ke baad, aapka deposit aapke profile mein dikhega.\n\n3️⃣ <b>Apna Balance Track Karo</b>\n • My Profile section mein aap apna deposit, active investments, aur profit dekhenge.\n\n4️⃣ <b>Investing Shuru Karo</b>\n • AI trading ko activate karne ke liye, Invest button dabayein aur period chunen.\n\n🔒 <b>Mahatvapoorn</b>\n • Sabhi funds aapke wallet se secure hain.\n • Withdrawal sirf usi wallet mein hoga jiska use deposit ke liye kiya gaya tha.\n • Beta testing chal raha hai, isliye thode delays ho sakte hain.",
                                        "🎉 <b>Регистрация завершена!</b>\n\n📖 <b>Краткое руководство:</b>\n\n1️⃣ <b>Пополните баланс</b>\n • Введите сумму в USDT (TRC20) и подтвердите.\n\n2️⃣ <b>Дождитесь подтверждения</b>\n • После подтверждения транзакции пополнение появится в вашем профиле.\n\n3️⃣ <b>Отслеживайте баланс</b>\n • В разделе профиля вы увидите депозит, активные инвестиции и прибыль.\n\n4️⃣ <b>Начните инвестировать</b>\n • Чтобы активировать AI-торговлю, нажмите Invest и выберите срок.\n\n🔒 <b>Важно</b>\n • Все средства надежно привязаны к вашему кошельку.\n • Вывод возможен только на тот же кошелек, который использовался для пополнения.\n • Поскольку это бета-тестирование, возможны небольшие задержки.",
                                        "🎉 <b>ثبت نام با موفقیت انجام شد!</b>\n\n📖 <b>راهنمای شروع سریع:</b>\n\n1️⃣ <b>واریز انجام دهید</b>\n • مبلغ را به USDT (TRC20) وارد کرده و تایید کنید.\n\n2️⃣ <b>منتظر تایید بمانید</b>\n • پس از تایید تراکنش، واریز شما در پروفایل نمایش داده می شود.\n\n3️⃣ <b>موجودی خود را دنبال کنید</b>\n • در بخش پروفایل، واریز، سرمایه گذاری های فعال و سود را خواهید دید.\n\n4️⃣ <b>سرمایه گذاری را شروع کنید</b>\n • برای فعال سازی AI trading، روی Invest بزنید و مدت را انتخاب کنید.\n\n🔒 <b>مهم</b>\n • همه وجوه با امنیت کامل به کیف پول شما متصل است.\n • برداشت فقط به همان کیف پولی انجام می شود که برای واریز استفاده شده است.\n • از آنجا که این نسخه بتاست، ممکن است تاخیرهای کوچکی رخ دهد.",
                                        "🎉 <b>تم التسجيل بنجاح!</b>\n\n📖 <b>دليل البدء السريع:</b>\n\n1️⃣ <b>قم بالإيداع</b>\n • أدخل المبلغ بـ USDT (TRC20) ثم قم بالتأكيد.\n\n2️⃣ <b>انتظر التأكيد</b>\n • بعد تأكيد المعاملة سيظهر الإيداع في ملفك الشخصي.\n\n3️⃣ <b>تابع رصيدك</b>\n • في قسم الملف الشخصي ستشاهد الإيداع والاستثمارات النشطة والأرباح.\n\n4️⃣ <b>ابدأ الاستثمار</b>\n • لتفعيل التداول بالذكاء الاصطناعي، اضغط على Invest واختر الفترة.\n\n🔒 <b>مهم</b>\n • جميع الأموال مرتبطة بمحفظتك بأمان.\n • السحب متاح فقط إلى نفس المحفظة المستخدمة في الإيداع.\n • نظراً لأن هذه نسخة تجريبية، فقد تحدث بعض التأخيرات البسيطة.",
                                        "🎉 <b>注册成功！</b>\n\n📖 <b>快速开始指南：</b>\n\n1️⃣ <b>进行充值</b>\n • 输入 USDT (TRC20) 金额并确认。\n\n2️⃣ <b>等待确认</b>\n • 交易确认后，充值会显示在您的个人资料中。\n\n3️⃣ <b>追踪余额</b>\n • 在 My Profile 中您可以看到充值、活跃投资和收益。\n\n4️⃣ <b>开始投资</b>\n • 如需启用 AI 交易，请点击 Invest 并选择周期。\n\n🔒 <b>重要提示</b>\n • 所有资金都会安全地绑定到您的钱包。\n • 提现只能退回到用于充值的同一钱包。\n • 由于目前处于测试阶段，可能会出现少量延迟。");

                // Створюємо inline кнопку
                var inlineKeyboard = new InlineKeyboardMarkup(new[]
                {
                    new[]
                    {
                        InlineKeyboardButton.WithCallbackData(
                            text: T(lang, "✅ I Acknowledge", "✅ Main Samjha", "✅ Я понял", "✅ متوجه شدم", "✅ فهمت", "✅ 我已知晓"),
                            callbackData: "read_instructions"
                        )
                    }
                });

                // Відправляємо повідомлення з кнопкою
                await bot.SendMessage(chatId, instructions, parseMode: ParseMode.Html, replyMarkup: inlineKeyboard);

                _pendingUsernames.Remove(chatId);
                _pendingPasswords.Remove(chatId);
                await _userStateService.ClearStateAsync(chatId);
            }
            else
            {
                // Якщо реєстрація не вдалася, очищаємо стани і показуємо головне меню
                _pendingUsernames.Remove(chatId);
                _pendingPasswords.Remove(chatId);
                await _userStateService.ClearStateAsync(chatId);
                await ShowMainMenuAsync(chatId);
            }

            return;
        }

// =================== New User Flow ===================
        if (user == null || !user.IsAuthorized)
        {
            var languageKeyboard = new InlineKeyboardMarkup(BuildLanguageSelectionButtons("lang_"));
            string welcomeMessage = _localizationService.GetLanguageSelectionWelcomeText();

            await _botClient.SendMessage(
                chatId: chatId,
                text: welcomeMessage,
                parseMode: ParseMode.Html,
                replyMarkup: languageKeyboard
            );
            return;
        }

        // Обробка текстових повідомлень для зміни логіну та пароля
        var currentState = await _userStateService.GetStateAsync(chatId);

        if (currentState == UserAction.WaitingForNewLogin)
        {
            var newLogin = update.Message.Text.Trim();

            // Перевірка на мінімальну довжину логіну
            if (newLogin.Length < 3)
            {
                await _botClient.SendMessage(chatId,
                    T(lang,
                        "❌ Login must be at least 3 characters long. Please try again:",
                        "❌ Login kam se kam 3 characters ka hona chahiye. Phir se prayas karen:",
                        "❌ Логин должен содержать не менее 3 символов. Попробуйте снова:",
                        "❌ نام کاربری باید حداقل 3 کاراکتر داشته باشد. دوباره تلاش کنید:",
                        "❌ يجب أن يتكون اسم الدخول من 3 أحرف على الأقل. حاول مرة أخرى:",
                        "❌ 登录名至少需要 3 个字符。请重试："));
                return;
            }

            var success = await _settingsService.UpdateUserLoginAsync(chatId, newLogin);

            if (success)
            {
                user.Username = newLogin;
            }

            await _userStateService.ClearStateAsync(chatId);
            await ShowMainMenuAsync(chatId); // Повертаємося в меню
            return;
        }

        if (currentState == UserAction.WaitingForNewPassword)
        {
            var newPassword = update.Message.Text.Trim();

            // Перевірка на мінімальну довжину пароля
            if (newPassword.Length < 4)
            {
                await _botClient.SendMessage(chatId,
                    T(lang,
                        "❌ Password must be at least 4 characters long. Please try again:",
                        "❌ Password kam se kam 4 characters ka hona chahiye. Phir se prayas karen:",
                        "❌ Пароль должен содержать не менее 4 символов. Попробуйте снова:",
                        "❌ رمز عبور باید حداقل 4 کاراکتر داشته باشد. دوباره تلاش کنید:",
                        "❌ يجب أن تتكون كلمة المرور من 4 أحرف على الأقل. حاول مرة أخرى:",
                        "❌ 密码长度至少需要 4 个字符。请重试："));
                return;
            }

            var success = await _settingsService.UpdateUserPasswordAsync(chatId, newPassword);

            await _userStateService.ClearStateAsync(chatId);
            await ShowMainMenuAsync(chatId); // Повертаємося в меню
            return;
        }


        if (state == UserAction.WaitingDepositAmount)
        {
            // Перевіряємо, чи введено число

            var normalizedText = msgText.Replace(',', '.');

            // Спроба парсингу з InvariantCulture
            if (!decimal.TryParse(normalizedText, NumberStyles.Any, CultureInfo.InvariantCulture,
                    out decimal amount))
            {
                await SendMessageWithBackButton(
                    chatId,
                    T(lang,
                        "❌ Invalid number format. Please use: 4.70 or 4,70",
                        "❌ Galat number format. Kripya use karein: 4.70 ya 4,70",
                        "❌ Неверный формат числа. Используйте: 4.70 или 4,70",
                        "❌ فرمت عدد نامعتبر است. لطفاً از 4.70 یا 4,70 استفاده کنید",
                        "❌ تنسيق الرقم غير صالح. يرجى استخدام 4.70 أو 4,70",
                        "❌ 数字格式无效。请使用：4.70 或 4,70")
                );
                return;
            }

            // Перевірка на позитивне число
            if (amount <= 0)
            {
                await SendMessageWithBackButton(
                    chatId,
                    T(lang, "❌ Amount must be greater than 0.", "❌ Raash 0 se zyada honi chahiye.", "❌ Сумма должна быть больше 0.", "❌ مبلغ باید بیشتر از 0 باشد.", "❌ يجب أن يكون المبلغ أكبر من 0.", "❌ 金额必须大于 0。")
                );
                return;
            }

            // Перевірка лімітів
            if (amount < 1)
            {
                await SendMessageWithBackButton(
                    chatId,
                    T(lang, "❌ Minimum deposit is 1 USDT.", "❌ Minimum deposit 1 USDT hai.", "❌ Минимальное пополнение - 1 USDT.", "❌ حداقل واریز 1 USDT است.", "❌ الحد الأدنى للإيداع هو 1 USDT.", "❌ 最低充值金额为 1 USDT。")
                );
                return;
            }

            if (amount > 100000000)
            {
                await SendMessageWithBackButton(
                    chatId,
                    T(lang, "❌ Maximum deposit is 100000000 USDT.", "❌ Maximum deposit 100000000 USDT hai.", "❌ Максимальное пополнение - 100000000 USDT.", "❌ حداکثر واریز 100000000 USDT است.", "❌ الحد الأقصى للإيداع هو 100000000 USDT.", "❌ 最大充值金额为 100000000 USDT。")
                );
                return;
            }

            // Коректна сума - продовжуємо обробку
            _pendingDeposits[chatId] = amount;


            // Створюємо кнопки
            var buttons = new List<InlineKeyboardButton[]>();

// Кнопка копіювання Primary Wallet
            buttons.Add(new[]
            {
                InlineKeyboardButton.WithCopyText(
                        T(lang, "💼 Primary Wallet", "💼 Praimari Wallet", "💼 Основной кошелек", "💼 کیف پول اصلی", "💼 المحفظة الأساسية", "💼 主钱包"),
                    FixedWalletAddresses.First().Value)
            });
            // Кнопка перегляду QR-коду
            buttons.Add(new[]
            {
                InlineKeyboardButton.WithUrl(
                        T(lang, "📱 Pay with QR Code", "📱 QR Code se Pay Karein", "📱 Оплатить через QR-код", "📱 پرداخت با کد QR", "📱 الدفع عبر رمز QR", "📱 使用二维码支付"),
                    $"https://wallets-copy.netlify.app/?address={Uri.EscapeDataString(FixedWalletAddresses.First().Value)}&amount={Uri.EscapeDataString(amount.ToString())}")
            });

// Кнопка підтвердження оплати
            buttons.Add(new[]
            {
                InlineKeyboardButton.WithCallbackData(
                        T(lang, "✅ I have paid", "✅ Maine payment kar diya", "✅ Я оплатил", "✅ پرداخت انجام شد", "✅ لقد دفعت", "✅ 我已付款"),
                    "confirm_payment")
            });

            buttons.Add(new[]
            {
                InlineKeyboardButton.WithCallbackData(
                        T(lang, "❓ Need Help?", "❓ Madad Chahiye?", "❓ Нужна помощь?", "❓ به کمک نیاز دارید؟", "❓ هل تحتاج إلى مساعدة؟", "❓ 需要帮助吗？"),
                    "support_help")
            });

// Кнопка назад у меню
            buttons.Add(new[]
            {
                InlineKeyboardButton.WithCallbackData(
                        T(lang, "🔙 Back to Menu", "🔙 Menu par wapas", "🔙 Назад в меню", "🔙 بازگشت به منو", "🔙 العودة إلى القائمة", "🔙 返回菜单"),
                    "back_to_menu")
            });


// Повідомлення для користувача без показу адрес
                        string depositMsg = T(
                                lang,
                                $"💳 Deposit amount: {amount} USDT\n\n⚠️ *Important: Send USDT only through TRC20 crypto network*\n\n👇 Copy one of the wallets below and make the transfer:",
                                $"💳 Deposit amount: {amount} USDT\n\n⚠️ *Important: Sirf TRC20 crypto network ke through USDT bhejen*\n\n👇 Niche diye gaye wallet me se ek par funds bhejen:",
                                $"💳 Сумма пополнения: {amount} USDT\n\n⚠️ *Важно: отправляйте USDT только через сеть TRC20*\n\n👇 Скопируйте один из кошельков ниже и выполните перевод:",
                                $"💳 مبلغ واریز: {amount} USDT\n\n⚠️ *مهم: فقط از شبکه TRC20 برای ارسال USDT استفاده کنید*\n\n👇 یکی از کیف پول های زیر را کپی کرده و انتقال را انجام دهید:",
                                $"💳 مبلغ الإيداع: {amount} USDT\n\n⚠️ *مهم: أرسل USDT فقط عبر شبكة TRC20*\n\n👇 انسخ أحد العناوين أدناه وأكمل التحويل:",
                                $"💳 充值金额：{amount} USDT\n\n⚠️ *重要：请仅通过 TRC20 网络发送 USDT*\n\n👇 请复制下方任一钱包地址并完成转账：");

// Відправка повідомлення з кнопками
            await bot.SendMessage(
                chatId,
                depositMsg,
                parseMode: ParseMode.Markdown,
                replyMarkup: new InlineKeyboardMarkup(buttons)
            );

// Очищаємо стан очікування суми
            await _userStateService.ClearStateAsync(chatId);
            return;
        }

        if (state == UserAction.WaitingWithdrawAmount)
        {
            // Перевіряємо, чи введено число з підтримкою різних культур
            if (!decimal.TryParse(msgText, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal amount) ||
                amount <= 0)
            {
                await SendMessageWithBackButton(
                    chatId,
                    T(lang, "❗ Enter a valid number for withdrawal.", "❗ Kripya withdrawal ke liye sahi sankhya darj karein.", "❗ Введите корректное число для вывода.", "❗ لطفاً مبلغ معتبری برای برداشت وارد کنید.", "❗ أدخل رقماً صالحاً للسحب.", "❗ 请输入有效的提现金额。")
                );
                return;
            }

            // Отримуємо баланс користувача
            var balance = await _profileService.GetBalanceAsync(chatId);

            if (balance < amount)
            {
                await SendMessageWithBackButton(
                    chatId,
                    T(lang, "❌ Insufficient balance to withdraw this amount.", "❌ Aapke account me is amount ko withdraw karne ke liye kaafi balance nahi hai.", "❌ Недостаточно средств для вывода этой суммы.", "❌ موجودی کافی برای برداشت این مبلغ ندارید.", "❌ الرصيد غير كافٍ لسحب هذا المبلغ.", "❌ 您的余额不足，无法提取该金额。")
                );
                return;
            }

            try
            {
                // Виконуємо операцію виведення
                var newBalance = await _operationService.WithdrawAsync(chatId, amount);

                var msg = T(
                    lang,
                    $"✅ Withdrawal request for {amount} USDT received.\n\n⏳ Funds will be transferred within 12 hours.",
                    $"✅ Aapka withdrawal request {amount} USDT ke liye receive ho gaya hai.\n\n⏳ Funds 12 ghanto ke andar transfer ho jayenge.",
                    $"✅ Запрос на вывод {amount} USDT получен.\n\n⏳ Средства будут переведены в течение 12 часов.",
                    $"✅ درخواست برداشت {amount} USDT شما دریافت شد.\n\n⏳ وجه حداکثر طی 12 ساعت منتقل می شود.",
                    $"✅ تم استلام طلب السحب بقيمة {amount} USDT.\n\n⏳ سيتم تحويل الأموال خلال 12 ساعة.",
                    $"✅ 已收到您提取 {amount} USDT 的请求。\n\n⏳ 资金将在 12 小时内转出。"
                );

                await bot.SendMessage(chatId, msg, parseMode: ParseMode.Html);

                // Скидаємо стан користувача
                await _userStateService.SetStateAsync(chatId, UserAction.None);
                await ShowMainMenuAsync(chatId);
            }
            catch (Exception ex)
            {
                await bot.SendMessage(chatId, $"❌ Error: {ex.Message}");
            }
        }

        if (state == UserAction.WaitingInvestAmount)
        {
            if (!_userDurations.TryGetValue(chatId, out var duration))
            {
                await SendMessageWithBackButton(chatId, "❗ Please select duration again.");
                return;
            }

            // Нормалізуємо введення (замінюємо кому на крапку)
            var normalizedText = msgText.Replace(',', '.');

            // Використовуємо InvariantCulture для парсингу
            if (!decimal.TryParse(normalizedText, NumberStyles.Any, CultureInfo.InvariantCulture, out var amount) ||
                amount <= 0)
            {
                await SendMessageWithBackButton(
                    chatId,
                    T(lang, "❗ Enter a valid positive number for investment (e.g., 4.70 or 4,70).", "❗ Kripya investment ke liye sahi sankhya darj karein (jaise, 4.70 ya 4,70).", "❗ Введите корректное положительное число для инвестиции (например, 4.70 или 4,70).", "❗ لطفاً عدد مثبت معتبری برای سرمایه گذاری وارد کنید (مثلاً 4.70 یا 4,70).", "❗ أدخل رقماً موجباً صالحاً للاستثمار (مثل 4.70 أو 4,70).", "❗ 请输入有效的正数投资金额（例如 4.70 或 4,70）。")
                );
                return;
            }

            try
            {
                decimal percent = GetRandomPercent(duration);
                int durationHours = duration switch
                {
                    InvestmentDuration.TwoDays => 48,
                    InvestmentDuration.OneWeek => 168,
                    InvestmentDuration.TwoWeeks => 336,
                    _ => throw new ArgumentOutOfRangeException()
                };

                // Оновлений розрахунок прибутку з точним терміном
                decimal totalProfit = Math.Round(amount * percent / 100m, 2);
                decimal dailyProfit = totalProfit / (durationHours / 24m);
                decimal weeklyProfit = totalProfit / (durationHours / 168m);

                string durationText = duration switch
                {
                    InvestmentDuration.TwoDays => T(lang, "2 days", "2 din", "2 дня", "2 روز", "يومان", "2 天"),
                    InvestmentDuration.OneWeek => T(lang, "1 week", "1 hafta", "1 неделя", "1 هفته", "أسبوع واحد", "1 周"),
                    InvestmentDuration.TwoWeeks => T(lang, "2 weeks", "2 hafta", "2 недели", "2 هفته", "أسبوعان", "2 周"),
                    _ => string.Empty
                };

                                var investmentMsg = T(
                                        lang,
                                        $"🎯 Investment Successful!\n\n💰 Amount: {amount:0.00} USDT\n⏳ Duration: {durationText}\n📈 Interest rate: {percent:0.##}%\n💵 Total profit: {totalProfit:0.00} USDT\n\n📊 Profit breakdown:\n   • Per day: {dailyProfit:0.00} USDT\n   • Per week: {weeklyProfit:0.00} USDT",
                                        $"🎯 Nivesh safal!\n\n💰 Rakam: {amount:0.00} USDT\n⏳ Avadhi: {durationText}\n📈 Byaj dar: {percent:0.##}%\n💵 Kul munafa: {totalProfit:0.00} USDT\n\n📊 Munafa vitran:\n   • Prati din: {dailyProfit:0.00} USDT\n   • Prati hafta: {weeklyProfit:0.00} USDT",
                                        $"🎯 Инвестиция успешно создана!\n\n💰 Сумма: {amount:0.00} USDT\n⏳ Срок: {durationText}\n📈 Ставка: {percent:0.##}%\n💵 Общая прибыль: {totalProfit:0.00} USDT\n\n📊 Разбивка прибыли:\n   • В день: {dailyProfit:0.00} USDT\n   • В неделю: {weeklyProfit:0.00} USDT",
                                        $"🎯 سرمایه گذاری با موفقیت انجام شد!\n\n💰 مبلغ: {amount:0.00} USDT\n⏳ مدت: {durationText}\n📈 نرخ سود: {percent:0.##}%\n💵 سود کل: {totalProfit:0.00} USDT\n\n📊 جزئیات سود:\n   • روزانه: {dailyProfit:0.00} USDT\n   • هفتگی: {weeklyProfit:0.00} USDT",
                                        $"🎯 تم الاستثمار بنجاح!\n\n💰 المبلغ: {amount:0.00} USDT\n⏳ المدة: {durationText}\n📈 معدل الفائدة: {percent:0.##}%\n💵 إجمالي الربح: {totalProfit:0.00} USDT\n\n📊 تفاصيل الربح:\n   • يومياً: {dailyProfit:0.00} USDT\n   • أسبوعياً: {weeklyProfit:0.00} USDT",
                                        $"🎯 投资成功！\n\n💰 金额：{amount:0.00} USDT\n⏳ 周期：{durationText}\n📈 收益率：{percent:0.##}%\n💵 总利润：{totalProfit:0.00} USDT\n\n📊 收益拆分：\n   • 每日：{dailyProfit:0.00} USDT\n   • 每周：{weeklyProfit:0.00} USDT");

                // Отримуємо поточний баланс користувача для повідомлення про помилку
                user = await _botDbContext.Users.FirstOrDefaultAsync(u => u.TelegramId == chatId);

                // Виклик методу з обробкою помилок
                var (newBalance, errorMessage) =
                    await _operationService.CreateInvestmentAsync(chatId, amount, percent, durationHours);

                if (errorMessage != null)
                {
                    // Обробка помилок
                    string userFriendlyError = errorMessage switch
                    {
                        string s when s.Contains("Insufficient balance") => T(
                            lang,
                            $"❌ Insufficient Balance!\n\n💳 Your current balance: {user?.Balance:F2} USDT\n💰 Required for investment: {amount:F2} USDT\n🔺 You need: {(amount - (user?.Balance ?? 0)):F2} USDT more\n\n💸 Please deposit funds to continue",
                            $"❌ Paishe nahi hai!\n\n💳 Aapke paas: {user?.Balance:F2} USDT\n💰 Investment ke liye chahiye: {amount:F2} USDT\n🔺 Aur chahiye: {(amount - (user?.Balance ?? 0)):F2} USDT\n\n💸 Kripya paise jama karein",
                            $"❌ Недостаточно средств!\n\n💳 Ваш текущий баланс: {user?.Balance:F2} USDT\n💰 Нужно для инвестиции: {amount:F2} USDT\n🔺 Не хватает: {(amount - (user?.Balance ?? 0)):F2} USDT\n\n💸 Пожалуйста, пополните баланс",
                            $"❌ موجودی کافی نیست!\n\n💳 موجودی فعلی شما: {user?.Balance:F2} USDT\n💰 مبلغ مورد نیاز برای سرمایه گذاری: {amount:F2} USDT\n🔺 شما نیاز دارید: {(amount - (user?.Balance ?? 0)):F2} USDT بیشتر\n\n💸 لطفاً برای ادامه واریز کنید",
                            $"❌ الرصيد غير كاف!\n\n💳 رصيدك الحالي: {user?.Balance:F2} USDT\n💰 المطلوب للاستثمار: {amount:F2} USDT\n🔺 تحتاج إلى: {(amount - (user?.Balance ?? 0)):F2} USDT إضافية\n\n💸 يرجى إيداع الأموال للمتابعة",
                            $"❌ 余额不足！\n\n💳 您当前余额：{user?.Balance:F2} USDT\n💰 投资所需金额：{amount:F2} USDT\n🔺 还需要：{(amount - (user?.Balance ?? 0)):F2} USDT\n\n💸 请先充值后继续"
                        ),
                        "User not found" => T(lang, "❌ User profile not found. Please try again later", "❌ Profile nahi mila. Phir se koshish karein", "❌ Профиль пользователя не найден. Попробуйте позже", "❌ پروفایل کاربر پیدا نشد. لطفاً بعداً دوباره تلاش کنید", "❌ لم يتم العثور على ملف المستخدم. حاول مرة أخرى لاحقاً", "❌ 未找到用户资料，请稍后重试"),
                        "Investment amount must be positive" => T(lang, "❌ Please enter a valid investment amount", "❌ Sahi investment rakam daalein", "❌ Пожалуйста, введите корректную сумму инвестиции", "❌ لطفاً مبلغ سرمایه گذاری معتبری وارد کنید", "❌ يرجى إدخال مبلغ استثمار صحيح", "❌ 请输入有效的投资金额"),
                        _ => T(lang, $"❌ Error: {errorMessage}", $"❌ Error: {errorMessage}", $"❌ Ошибка: {errorMessage}", $"❌ خطا: {errorMessage}", $"❌ خطأ: {errorMessage}", $"❌ 错误：{errorMessage}")
                    };

                    await bot.SendMessage(chatId, userFriendlyError);

                    // Кнопка для поповнення
                    if (errorMessage.Contains("Insufficient balance"))
                    {
                        var depositKeyboard = new InlineKeyboardMarkup(new[]
                        {
                            new[]
                            {
                                InlineKeyboardButton.WithCallbackData(
                                    T(lang, "💳 Deposit Funds", "💳 Paise Jama Karein", "💳 Пополнить баланс", "💳 واریز وجه", "💳 إيداع الأموال", "💳 充值资金"),
                                    "deposit")
                            }
                        });

                        await bot.SendMessage(chatId,
                            T(lang, "Click to add funds:", "Paise jama karne ke liye click karein:", "Нажмите, чтобы пополнить баланс:", "برای افزودن وجه کلیک کنید:", "اضغط لإضافة الأموال:", "点击以充值资金："),
                            replyMarkup: depositKeyboard);
                    }

                    return;
                }

                // Якщо успішно
                await bot.SendMessage(chatId, investmentMsg, parseMode: ParseMode.Markdown);

                _userDurations.Remove(chatId);
                await _userStateService.SetStateAsync(chatId, UserAction.None);
                await ShowMainMenuAsync(chatId);
            }
            catch (Exception ex)
            {
                // Загальна помилка
                string errorMsg = T(lang, "❌ Error creating investment. Please try again later.", "❌ Investment nahi ho paya. Phir se koshish karein.", "❌ Ошибка создания инвестиции. Пожалуйста, попробуйте позже.", "❌ خطا در ایجاد سرمایه گذاری. لطفاً بعداً دوباره تلاش کنید.", "❌ حدث خطأ أثناء إنشاء الاستثمار. حاول مرة أخرى لاحقاً.", "❌ 创建投资时出错，请稍后重试。");

                await bot.SendMessage(chatId, errorMsg);
                _logger.LogError(ex, "Error in investment creation");
            }
        }


        if (_waitingInvestmentUserId.TryGetValue(chatId, out bool waiting) && waiting)
        {
            _waitingInvestmentUserId.Remove(chatId); // очищаємо стан

            if (long.TryParse(msgText, out long targetUserId))
            {
                if (_investmentAdminService != null)
                {
                    await _investmentAdminService.ShowUserInvestmentsAsync(chatId, targetUserId);

                    // Чекаємо невелику затримку, щоб повідомлення сервісу відправилися першими
                    await Task.Delay(500);

                    // Додаємо кнопку "Назад" після всіх інвестицій
                    var backKeyboard = new InlineKeyboardMarkup(new[]
                    {
                        new[]
                        {
                            InlineKeyboardButton.WithCallbackData("🔙 Back to Menu", "back_to_menu")
                        }
                    });

                    string backText = T(lang, "Return to menu:", "Menu par wapas jao:", "Вернуться в меню:", "بازگشت به منو:", "العودة إلى القائمة:", "返回菜单：");

                    await bot.SendMessage(chatId, backText, replyMarkup: backKeyboard);
                }
                else
                {
                    await bot.SendMessage(chatId, "❌ Investment service not available.");
                }
            }
            else
            {
                string errorMsg = T(GetUserLanguageCode(user), "❌ Invalid user ID. Please enter only digits.", "❌ Galat user ID. Sirf ank daalein.", "❌ Неверный ID пользователя. Введите только цифры.", "❌ شناسه کاربر نامعتبر است. فقط عدد وارد کنید.", "❌ معرف المستخدم غير صالح. يرجى إدخال أرقام فقط.", "❌ 用户 ID 无效，请只输入数字。");

                await bot.SendMessage(chatId, errorMsg);
            }

            return; // важливо: виходимо після обробки
        }

        var userState = await _userStateService.GetStateAsync(message.From.Id);

        if (userState == UserAction.WaitingForNewWalletAddress)
        {
            try
            {
                // ОНОВЛЮЄМО БД!
                var success = await _settingsService.UpdateUserWalletAddressAsync(
                    message.Chat.Id,
                    message.Text
                );

                var userLanguage = await _manageService.GetUserLanguageAsync(message.From.Id);

                if (success)
                {
                    await _botClient.SendMessage(
                        message.Chat.Id,
                        T(userLanguage,
                            "✅ *Wallet address updated successfully!*\n\nYour new wallet address has been saved.",
                            "✅ *Wallet address safalta purvak update ho gaya!*\n\nAapka naya wallet address save ho gaya hai.",
                            "✅ *Адрес кошелька успешно обновлен!*\n\nВаш новый адрес кошелька сохранен.",
                            "✅ *آدرس کیف پول با موفقیت به روز شد!*\n\nآدرس جدید کیف پول شما ذخیره شد.",
                            "✅ *تم تحديث عنوان المحفظة بنجاح!*\n\nتم حفظ عنوان محفظتك الجديد.",
                            "✅ *钱包地址更新成功！*\n\n您的新钱包地址已保存。"),
                        parseMode: ParseMode.Markdown
                    );
                }
                else
                {
                    await _botClient.SendMessage(
                        message.Chat.Id,
                        T(userLanguage,
                            "❌ *Invalid wallet address*\n\nPlease check the format and try again.",
                            "❌ *Galat wallet address*\n\nKripya format check karen aur phir se try karen.",
                            "❌ *Некорректный адрес кошелька*\n\nПроверьте формат и попробуйте снова.",
                            "❌ *آدرس کیف پول نامعتبر است*\n\nلطفاً فرمت را بررسی کرده و دوباره تلاش کنید.",
                            "❌ *عنوان المحفظة غير صالح*\n\nيرجى التحقق من التنسيق والمحاولة مرة أخرى.",
                            "❌ *钱包地址无效*\n\n请检查格式后重试。"),
                        parseMode: ParseMode.Markdown
                    );
                }

                // Показуємо меню налаштувань знову
                await _settingsService.ShowSettingsMenuAsync(message.Chat.Id, userLanguage);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating wallet address for user {UserId}", message.From.Id);
                await _botClient.SendMessage(
                    message.Chat.Id,
                    T(userLanguage,
                        "❌ Error updating wallet address. Please try again later.",
                        "❌ Wallet address update nahi ho paya. Kripya baad mein phir try karein.",
                        "❌ Ошибка обновления адреса кошелька. Попробуйте позже.",
                        "❌ خطا در به روزرسانی آدرس کیف پول. لطفاً بعداً دوباره تلاش کنید.",
                        "❌ حدث خطأ أثناء تحديث عنوان المحفظة. حاول مرة أخرى لاحقاً.",
                        "❌ 更新钱包地址时出错，请稍后再试。")
                );
            }
            finally
            {
                // Очищаємо стан незалежно від результату
                await _userStateService.ClearStateAsync(message.From.Id);
            }

            return; // Важливо: не обробляти далі як звичайне повідомлення
        }

        if (currentState == UserAction.WaitingBonusTelegramId ||
            currentState == UserAction.WaitingBonusActivate ||
            currentState == UserAction.WaitingBonusDeactivate)
        {
            // Визначаємо дію на основі стану
            bool? forceAction = null;
            if (currentState == UserAction.WaitingBonusActivate)
                forceAction = true;
            else if (currentState == UserAction.WaitingBonusDeactivate)
                forceAction = false;

            // Перевіряємо чи введено числовий Telegram ID
            if (!long.TryParse(message.Text, out long targetTelegramId))
            {
                await _botClient.SendMessage(
                    chatId: chatId,
                    text:
                    "❌ <b>Invalid format</b>\n\nPlease enter a valid Telegram ID (numeric value only).\n\nExample: <code>123456789</code>",
                    parseMode: ParseMode.Html,
                    replyMarkup: new InlineKeyboardMarkup(new[]
                    {
                        new[]
                        {
                            InlineKeyboardButton.WithCallbackData("↩️ Back to Bonus Menu", "admin_toggle_bonus")
                        }
                    })
                );
                await _userStateService.SetStateAsync(chatId, UserAction.None);
                return;
            }

            // Додаємо індикатор завантаження
            var processingMessage = await _botClient.SendMessage(
                chatId: chatId,
                text: "⏳ <b>Processing request...</b>",
                parseMode: ParseMode.Html
            );

            try
            {
                var targetUser = await _manageService.GetUserByTelegramIdAsync(targetTelegramId);
                if (targetUser == null)
                {
                    await _botClient.EditMessageText(
                        chatId: chatId,
                        messageId: processingMessage.MessageId,
                        text: "❌ <b>User Not Found</b>\n\nNo user found with Telegram ID: <code>" +
                              targetTelegramId +
                              "</code>\n\nPlease check the ID and try again.",
                        parseMode: ParseMode.Html,
                        replyMarkup: new InlineKeyboardMarkup(new[]
                        {
                            new[]
                            {
                                InlineKeyboardButton.WithCallbackData("↩️ Back to Bonus Menu", "admin_toggle_bonus")
                            }
                        })
                    );
                    await _userStateService.SetStateAsync(chatId, UserAction.None);
                    return;
                }

                // Визначаємо новий статус
                bool newStatus;
                if (forceAction.HasValue)
                {
                    newStatus = forceAction.Value; // Активація або деактивація
                }
                else
                {
                    newStatus = !targetUser.Has25PercentBonus; // Перемикач (стара логіка)
                }

                bool success = await _referralService.Set25PercentBonusAsync(targetUser.Id, newStatus);

                if (success)
                {
                    string status = newStatus ? "🟢 ACTIVATED" : "🔴 DEACTIVATED";
                    string emoji = newStatus ? "🎉" : "ℹ️";
                    string actionText = newStatus ? "activated" : "deactivated";

                    // Повідомлення для адміна
                    await _botClient.EditMessageText(
                        chatId: chatId,
                        messageId: processingMessage.MessageId,
                        text: $"""
                               ✅ <b>Bonus Status Updated</b>
                               ───────────────────
                               👤 <b>User:</b> <code>{targetTelegramId}</code>
                               📛 <b>Username:</b> @{targetUser.Username ?? "N/A"}
                               💰 <b>Bonus:</b> {status}
                               🕒 <b>Time:</b> {DateTime.Now:yyyy-MM-dd HH:mm:ss}
                               ───────────────────
                               {emoji} <i>25% referral bonus has been successfully {actionText}.</i>
                               """,
                        parseMode: ParseMode.Html,
                        replyMarkup: new InlineKeyboardMarkup(new[]
                        {
                            new[]
                            {
                                InlineKeyboardButton.WithCallbackData("🎯 Manage Another Bonus",
                                    "admin_toggle_bonus"),
                                InlineKeyboardButton.WithCallbackData("📋 Admin Panel", "admin_panel_back")
                            }
                        })
                    );

                    // Повідомлення для користувача
                    try
                    {
                        var targetUserLang = GetUserLanguageCode(targetUser);
                        string messageText;

                        messageText = T(
                            targetUserLang,
                            $"""
                               🎯 <b>Bonus Status Update</b>
                               ───────────────────
                               💰 25% Referral Bonus: {(newStatus ? "🟢 ACTIVE" : "🔴 INACTIVE")}
                               📋 <b>Action:</b> {(newStatus ? "Activated" : "Deactivated")}
                               🕒 <b>Time:</b> {DateTime.Now:yyyy-MM-dd HH:mm:ss}
                               ───────────────────
                               {(newStatus ?
                                   "🎉 Congratulations! You now receive <b>25% bonus</b> from your referral investments!" :
                                   "ℹ️ Your 25% referral bonus has been deactivated.")}
                               """,
                            $"""
                               🎯 <b>Bonus Status Update</b>
                               ───────────────────
                               💰 25% Referral Bonus: {(newStatus ? "🟢 ACTIVE" : "🔴 INACTIVE")}
                               📋 <b>Action:</b> {(newStatus ? "Activated" : "Deactivated")}
                               🕒 <b>Time:</b> {DateTime.Now:yyyy-MM-dd HH:mm:ss}
                               ───────────────────
                               {(newStatus ?
                                   "🎉 Badhai ho! Aapko ab apne referral investments se <b>25% bonus</b> milta hai!" :
                                   "ℹ️ Aapka 25% referral bonus band kar diya gaya.")}
                               """,
                            $"""
                               🎯 <b>Обновление статуса бонуса</b>
                               ───────────────────
                               💰 25% реферальный бонус: {(newStatus ? "🟢 АКТИВЕН" : "🔴 НЕАКТИВЕН")}
                               📋 <b>Действие:</b> {(newStatus ? "Активирован" : "Деактивирован")}
                               🕒 <b>Время:</b> {DateTime.Now:yyyy-MM-dd HH:mm:ss}
                               ───────────────────
                               {(newStatus ?
                                   "🎉 Поздравляем! Теперь вы получаете <b>25% бонус</b> с инвестиций ваших рефералов!" :
                                   "ℹ️ Ваш 25% реферальный бонус был деактивирован.")}
                               """,
                            $"""
                               🎯 <b>به روزرسانی وضعیت بонус</b>
                               ───────────────────
                               💰 بонус دعوت 25٪: {(newStatus ? "🟢 فعال" : "🔴 غیرفعال")}
                               📋 <b>اقدام:</b> {(newStatus ? "فعال شد" : "غیرفعال شد")}
                               🕒 <b>زمان:</b> {DateTime.Now:yyyy-MM-dd HH:mm:ss}
                               ───────────────────
                               {(newStatus ?
                                   "🎉 تبریک! اکنون از سرمایه گذاری های دعوت شدگان خود <b>25٪ بонус</b> دریافت می کنید!" :
                                   "ℹ️ بонус دعوت 25٪ شما غیرفعال شد.")}
                               """,
                            $"""
                               🎯 <b>تحديث حالة المكافأة</b>
                               ───────────────────
                               💰 مكافأة الإحالة 25%: {(newStatus ? "🟢 نشطة" : "🔴 غير نشطة")}
                               📋 <b>الإجراء:</b> {(newStatus ? "تم التفعيل" : "تم الإلغاء")}
                               🕒 <b>الوقت:</b> {DateTime.Now:yyyy-MM-dd HH:mm:ss}
                               ───────────────────
                               {(newStatus ?
                                   "🎉 تهانينا! أنت الآن تحصل على <b>مكافأة 25%</b> من استثمارات الإحالات الخاصة بك!" :
                                   "ℹ️ تم إلغاء تفعيل مكافأة الإحالة 25% الخاصة بك.")}
                               """,
                            $"""
                               🎯 <b>奖励状态更新</b>
                               ───────────────────
                               💰 25% 邀请奖励: {(newStatus ? "🟢 已启用" : "🔴 已停用")}
                               📋 <b>操作:</b> {(newStatus ? "已启用" : "已停用")}
                               🕒 <b>时间:</b> {DateTime.Now:yyyy-MM-dd HH:mm:ss}
                               ───────────────────
                               {(newStatus ?
                                   "🎉 恭喜！您现在可从邀请用户的投资中获得 <b>25%</b> 奖励！" :
                                   "ℹ️ 您的 25% 邀请奖励已被停用。")}
                               """
                        );

                        // Надсилаємо повідомлення користувачу
                        await _botClient.SendMessage(
                            chatId: targetTelegramId,
                            text: messageText,
                            parseMode: ParseMode.Html,
                            replyMarkup: new InlineKeyboardMarkup(new[]
                            {
                                new[]
                                {
                                    InlineKeyboardButton.WithCallbackData(T(targetUserLang, "🏠 Main Menu", "🏠 Main Menu", "🏠 Главное меню", "🏠 منوی اصلی", "🏠 القائمة الرئيسية", "🏠 主菜单"), "back_to_menu")
                                }
                            })
                        );
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Failed to notify user: {ex.Message}");
                    }
                }
                else
                {
                    await _botClient.EditMessageText(
                        chatId: chatId,
                        messageId: processingMessage.MessageId,
                        text: "❌ <b>Update Failed</b>\n\nFailed to update bonus status for user ID: <code>" +
                              targetTelegramId + "</code>\n\nPlease try again or contact technical support.",
                        parseMode: ParseMode.Html,
                        replyMarkup: new InlineKeyboardMarkup(new[]
                        {
                            new[]
                            {
                                InlineKeyboardButton.WithCallbackData("🔄 Try Again", "admin_toggle_bonus"),
                                InlineKeyboardButton.WithCallbackData("📋 Admin Panel", "admin_panel_back")
                            }
                        })
                    );
                }
            }
            catch (Exception ex)
            {
                await _botClient.EditMessageText(
                    chatId: chatId,
                    messageId: processingMessage.MessageId,
                    text:
                    "⚠️ <b>System Error</b>\n\nAn unexpected error occurred while processing your request.\n\nPlease try again later.",
                    parseMode: ParseMode.Html,
                    replyMarkup: new InlineKeyboardMarkup(new[]
                    {
                        new[]
                        {
                            InlineKeyboardButton.WithCallbackData("🔄 Try Again", "admin_toggle_bonus"),
                            InlineKeyboardButton.WithCallbackData("📋 Admin Panel", "admin_panel_back")
                        }
                    })
                );
                Console.WriteLine($"Error in bonus toggle: {ex.Message}");
            }

            await _userStateService.SetStateAsync(chatId, UserAction.None);

            // Видаляємо повідомлення про обробку через 5 секунд
            _ = Task.Delay(5000).ContinueWith(async _ =>
            {
                try
                {
                    await _botClient.DeleteMessage(chatId, processingMessage.MessageId);
                }
                catch
                {
                    // Ігноруємо помилки видалення
                }
            });
        }


        // =================== 8️⃣ Далі йде логіка основного бота (депозит, withdraw, invest, профіль тощо) ===================


        // =================== 6️⃣ Тут можна додавати логіку основного бота ===================
        // Наприклад: депозит, withdraw, invest, профіль, referral тощо
    }


    private async Task SendMessageWithBackButton(long chatId, string text, string lang = "English")
    {
        string backButtonText = _localizationService.IsEnglish(lang) ? "🔙 Back" : "🔙 Wapas";

        // Видаляємо hintText, оскільки він вже не потрібен
        var keyboard = new InlineKeyboardMarkup(
            InlineKeyboardButton.WithCallbackData(backButtonText, "back_to_menu")
        );

        await _botClient.SendMessage(
            chatId: chatId,
            text: text, // Без додавання hintText
            replyMarkup: keyboard,
            parseMode: ParseMode.Markdown
        );
    }

    private Task HandleErrorAsync(ITelegramBotClient botClient, Exception exception,
        CancellationToken cancellationToken)
    {
        Console.WriteLine($"❌ Telegram Bot Error: {exception}");
        return Task.CompletedTask;
    }

    private async Task ShowAuthOptions(long chatId, string lang)
    {
        var buttons = new InlineKeyboardMarkup(new[]
        {
            new[]
            {
                InlineKeyboardButton.WithCallbackData(
                    _localizationService.GetText(lang, "auth.register"),
                    "register"),
                InlineKeyboardButton.WithCallbackData(
                    _localizationService.GetText(lang, "auth.login"),
                    "login")
            }
        });

        await _botClient.SendMessage(chatId,
            _localizationService.GetText(lang, "auth.continuePrompt"),
            replyMarkup: buttons);
    }

    private async Task ShowMainMenuAsync(long chatId, int? messageId = null)
    {
        try
        {
            // ======= ДОДАЄМО ПЕРЕВІРКУ ІНВЕСТИЦІЙ ПЕРЕД ПОКАЗОМ МЕНЮ =======
            await _profileService.CheckInvestmentsForUser(chatId);

            // Отримуємо користувача (можливо з оновленим балансом після перевірки інвестицій)
            var user = await _manageService.GetUserByTelegramIdAsync(chatId);
            string lang = user?.PreferredLanguage ?? user?.Language ?? BotLanguageCodes.English;

            // Створюємо персоналізоване вітання з HTML-форматуванням
            string welcomeMessage = _localizationService.GetText(
                lang,
                "menu.welcome",
                EscapeHtml(user?.Username ?? (_localizationService.IsEnglish(lang) ? "friend" : "friend")));

            // Створюємо основні кнопки меню
            var mainMenuButtons = new List<InlineKeyboardButton[]>
            {
                new[]
                {
                    InlineKeyboardButton.WithCallbackData("💰 " + _localizationService.GetText(lang, "menu.deposit"),
                        "deposit"),
                    InlineKeyboardButton.WithCallbackData("📈 " + _localizationService.GetText(lang, "menu.invest"),
                        "invest")
                },
                new[]
                {
                    InlineKeyboardButton.WithCallbackData(
                        "👤 " + _localizationService.GetText(lang, "menu.profile"),
                        "my_profile"),
                    InlineKeyboardButton.WithCallbackData("💳 " + _localizationService.GetText(lang, "menu.withdraw"),
                        "withdraw")
                },
                new[]
                {
                    InlineKeyboardButton.WithCallbackData(
                        "🎁 " + _localizationService.GetText(lang, "menu.referral"), "referral_rewards"),
                    InlineKeyboardButton.WithCallbackData("🏆 " + _localizationService.GetText(lang, "menu.quests"),
                        "quests") // Просто кнопка квестів
                },
                new[]
                {
                    InlineKeyboardButton.WithCallbackData(
                        "🤖 " + _localizationService.GetText(lang, "menu.about"), "about"),
                    InlineKeyboardButton.WithCallbackData(
                        "❓ " + _localizationService.GetText(lang, "menu.support"),
                        "support_help")
                },
                new[]
                {
                    InlineKeyboardButton.WithCallbackData("⚙️ " + _localizationService.GetText(lang, "menu.settings"),
                        "settings"),
                }
            };

            // Відправляємо або редагуємо головне меню
            await EditOrSendAsync(
                chatId,
                messageId,
                welcomeMessage,
                parseMode: ParseMode.Html,
                replyMarkup: new InlineKeyboardMarkup(mainMenuButtons)
            );

            // Якщо користувач адмін - додатково відправляємо адмін-панель
            if (user?.Role == UserRole.Admin)
            {
                var adminButtons = new List<InlineKeyboardButton[]>
                    {
                        new[]
                        {
                            InlineKeyboardButton.WithCallbackData(
                                T(lang, "📥 Manage Deposits", "📥 Deposit Manage", "📥 Управление депозитами", "📥 مدیریت واریزها", "📥 إدارة الإيداعات", "📥 管理充值"),
                                "admin_manage_requests"),
                            InlineKeyboardButton.WithCallbackData(
                                T(lang, "📤 Manage Withdrawals", "📤 Withdrawal Manage", "📤 Управление выводами", "📤 مدیریت برداشت ها", "📤 إدارة السحوبات", "📤 管理提现"),
                                "admin_withdrawals")
                        },
                        new[]
                        {
                            InlineKeyboardButton.WithCallbackData(
                                T(lang, "📊 All Investments", "📊 Sabhi Nivesh", "📊 Все инвестиции", "📊 همه سرمایه گذاری ها", "📊 جميع الاستثمارات", "📊 所有投资"), "admin_investments"),
                            InlineKeyboardButton.WithCallbackData(
                                T(lang, "👤 User Investments", "👤 User Nivesh", "👤 Инвестиции пользователя", "👤 سرمایه گذاری های کاربر", "👤 استثمارات المستخدم", "👤 用户投资"),
                                "admin_investments_user")
                        },
                        new[]
                        {
                            InlineKeyboardButton.WithCallbackData(
                                T(lang, "🎯 Toggle 25% Bonus", "🎯 25% Bonus Manage", "🎯 Переключить бонус 25%", "🎯 تغییر بонус 25٪", "🎯 تبديل مكافأة 25%", "🎯 切换 25% 奖励"),
                                "admin_toggle_bonus")
                        },
                        new[]
                        {
                            InlineKeyboardButton.WithCallbackData("✅ Enable maintenance", "admin_enable_maintenance"),
                            InlineKeyboardButton.WithCallbackData("❌ Disable maintenance", "admin_disable_maintenance")
                        },
                        new[]
                        {
                        InlineKeyboardButton.WithCallbackData("✅ Delay Confirm", "admin_delay_confirm")
                    },
 
                new[]
                {
                    InlineKeyboardButton.WithCallbackData(
                        T(lang, "❌ Close Admin Panel", "❌ Admin Panel Band Karein", "❌ Закрыть админ-панель", "❌ بستن پنل ادمین", "❌ إغلاق لوحة الإدارة", "❌ 关闭管理面板"),
                        "delete_message")
                }
                };

                await _botClient.SendMessage(
                    chatId: chatId,
                    text: "🛠️ <b>Admin Panel</b>",
                    parseMode: ParseMode.Html,
                    replyMarkup: new InlineKeyboardMarkup(adminButtons)
                );
            }
        }
        catch (Exception ex)
        {
            // Логуємо помилку для діагностики
            Console.WriteLine($"Error in ShowMainMenuAsync: {ex.Message}");

            // Відправляємо просте повідомлення без форматування на випадок помилки
            await _botClient.SendMessage(
                chatId: chatId,
                text: "Welcome! Choose an option:",
                replyMarkup: new InlineKeyboardMarkup(new[]
                {
                    new[]
                    {
                        InlineKeyboardButton.WithCallbackData("💰 Deposit", "deposit"),
                        InlineKeyboardButton.WithCallbackData("📈 Invest", "invest")
                    }
                })
            );
        }
    }


    private string EscapeHtml(string text)
    {
        if (string.IsNullOrEmpty(text))
            return text;

        return text
            .Replace("&", "&amp;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;")
            .Replace("\"", "&quot;");
    }

    private async Task EditOrSendAsync(long chatId, int? messageId, string text, ParseMode? parseMode = null, InlineKeyboardMarkup? replyMarkup = null)
    {
        if (messageId.HasValue)
        {
            try
            {
                if (parseMode.HasValue)
                {
                    await _botClient.EditMessageText(
                        chatId: chatId,
                        messageId: messageId.Value,
                        text: text,
                        parseMode: parseMode.Value,
                        replyMarkup: replyMarkup
                    );
                }
                else
                {
                    await _botClient.EditMessageText(
                        chatId: chatId,
                        messageId: messageId.Value,
                        text: text,
                        replyMarkup: replyMarkup
                    );
                }
                return;
            }
            catch
            {
                // Fallback to sending new message if edit fails
            }
        }
        if (parseMode.HasValue)
        {
            await _botClient.SendMessage(
                chatId: chatId,
                text: text,
                parseMode: parseMode.Value,
                replyMarkup: replyMarkup
            );
        }
        else
        {
            await _botClient.SendMessage(
                chatId: chatId,
                text: text,
                replyMarkup: replyMarkup
            );
        }
    }

// Додайте цей метод для обробки команди "back_to_menu"
    private async Task HandleBackToMenu(long chatId)
    {
        await _userStateService.SetStateAsync(chatId, UserAction.None);
        await ShowMainMenuAsync(chatId);
    }


    private async Task ShowRegistrationRulesAsync(long chatId, string language)
    {
        string rulesText = _localizationService.GetText(language, "registration.rules");

        await _userStateService.SetStateAsync(chatId, UserAction.WaitingRulesAccept);

        var rulesKeyboard = new InlineKeyboardMarkup(new[]
        {
            new[]
            {
                InlineKeyboardButton.WithCallbackData(_localizationService.GetText(language, "registration.rulesAck"), "rules_accepted")
            },
        });

        await _botClient.SendMessage(chatId, rulesText, replyMarkup: rulesKeyboard,
            parseMode: ParseMode.Markdown);
    }

    private async Task HandleAcknowledgeGuideCallback(CallbackQuery callbackQuery, User user)
    {
        var chatId = callbackQuery.Message.Chat.Id;

        // Видаляємо inline кнопку
        await _botClient.EditMessageReplyMarkup(chatId, callbackQuery.Message.MessageId, null);

        // Фінальне привітання
        string welcomeMessage = _localizationService.GetText(user.PreferredLanguage ?? user.Language, "registration.ready");

        await _botClient.SendMessage(chatId, welcomeMessage);

        // Очищаємо тимчасові дані та стан
        _pendingUsernames.Remove(chatId);
        _pendingPasswords.Remove(chatId);
        await _userStateService.ClearStateAsync(chatId);

        // Показуємо головне меню
        await ShowMainMenuAsync(chatId);

        // Підтверджуємо CallbackQuery
        await _botClient.AnswerCallbackQuery(callbackQuery.Id);
    }

    private async Task CheckInvestmentsPeriodically()
    {
        if ((DateTime.UtcNow - _lastInvestmentCheck).TotalMinutes < 5)
            return;

        _lastInvestmentCheck = DateTime.UtcNow;

        try
        {
            // Беремо користувачів з активними інвестиціями
            var usersWithActiveInvestments = await _botDbContext.Users
                .Where(u => u.Investments.Any(i => i.IsActive))
                .Select(u => u.TelegramId)
                .ToListAsync();

            foreach (var telegramId in usersWithActiveInvestments)
            {
                // Використовуємо вже існуючий _profileService
                await _profileService.CheckInvestmentsForUser(telegramId);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Periodic investment check failed: {ex.Message}");
        }
    }

    private string GetProgressBar(decimal percentage, string language)
    {
        // Обмежуємо відсотки від 0 до 100
        var clampedPercentage = Math.Max(0, Math.Min(percentage, 100));

        var barLength = 10;
        var filled = (int)(clampedPercentage / 100 * barLength);
        var empty = barLength - filled;

        var bar = new string('█', filled) + new string('░', empty);

        return $"[{bar}]";
    }

// Метод для локалізації одиниць виміру
    private string GetLocalizedUnit(string unit, string language)
    {
        return _localizationService.NormalizeCode(language) switch
        {
            BotLanguageCodes.Hinglish => unit switch
            {
                "times" => "baar",
                "USDT" => "USDT",
                "investments" => "investments",
                "friends" => "dost",
                "days" => "din",
                _ => unit
            },
            BotLanguageCodes.Russian => unit switch
            {
                "times" => "раз",
                "USDT" => "USDT",
                "investments" => "инвестиций",
                "friends" => "друзей",
                "days" => "дней",
                _ => unit
            },
            BotLanguageCodes.Farsi => unit switch
            {
                "times" => "بار",
                "USDT" => "USDT",
                "investments" => "سرمایه گذاری",
                "friends" => "دوست",
                "days" => "روز",
                _ => unit
            },
            BotLanguageCodes.Arabic => unit switch
            {
                "times" => "مرة",
                "USDT" => "USDT",
                "investments" => "استثمارات",
                "friends" => "أصدقاء",
                "days" => "أيام",
                _ => unit
            },
            BotLanguageCodes.SimplifiedChinese => unit switch
            {
                "times" => "次",
                "USDT" => "USDT",
                "investments" => "笔投资",
                "friends" => "位朋友",
                "days" => "天",
                _ => unit
            },
            _ => unit
        };
    }

    private decimal GetQuestProgressPercentage(UserQuest userQuest)
    {
        if (userQuest.Quest == null || userQuest.Quest.TargetValue <= 0)
            return 0;

        var percentage = (userQuest.CurrentProgress / userQuest.Quest.TargetValue) * 100;
        return Math.Min(Math.Max(percentage, 0), 100);
    }

    private async Task HandleQuestCallback(CallbackQuery callback)
    {
        try
        {
            var userQuests = await _questService.GetUserQuestsAsync(callback.From.Id);
            var user = await _manageService.GetUserByTelegramIdAsync(callback.From.Id);
            var lang = GetUserLanguageCode(user);

            if (!userQuests.Any())
            {
                var noQuestsMsg = T(lang,
                    "🎯 <b>No quests available</b>\n\nCheck back later for new quests!",
                    "🎯 <b>Abhi koi quests available nahi hain</b>\n\nNaye quests ke liye baad mein check karein!",
                    "🎯 <b>Квесты пока недоступны</b>\n\nЗагляните позже за новыми квестами!",
                    "🎯 <b>در حال حاضر ماموریتی موجود نیست</b>\n\nبعداً برای ماموریت های جدید دوباره بررسی کنید!",
                    "🎯 <b>لا توجد مهام متاحة حالياً</b>\n\nتحقق لاحقاً من وجود مهام جديدة!",
                    "🎯 <b>当前没有可用任务</b>\n\n稍后再来查看新任务！");

                var noQuestsKeyboard = new InlineKeyboardMarkup(new[]
                {
                    new[]
                    {
                        InlineKeyboardButton.WithCallbackData(
                            T(lang, "🔄 Try Again", "🔄 Dobara Koshish Karo", "🔄 Попробовать снова", "🔄 دوباره تلاش کنید", "🔄 حاول مرة أخرى", "🔄 重试"),
                            "quests")
                    },
                    new[]
                    {
                        InlineKeyboardButton.WithCallbackData(
                            T(lang, "🔙 Back to Menu", "🔙 Wapas Menu", "🔙 Назад в меню", "🔙 بازگشت به منو", "🔙 العودة إلى القائمة", "🔙 返回菜单"),
                            "back_to_menu")
                    }
                });

                await _botClient.EditMessageText(
                    chatId: callback.Message.Chat.Id,
                    messageId: callback.Message.MessageId,
                    text: noQuestsMsg,
                    parseMode: ParseMode.Html,
                    replyMarkup: noQuestsKeyboard);
                return;
            }

            var message = T(lang,
                "🎯 <b>Your Quests</b>\n\n",
                "🎯 <b>Aapke Quests</b>\n\n",
                "🎯 <b>Ваши квесты</b>\n\n",
                "🎯 <b>ماموریت های شما</b>\n\n",
                "🎯 <b>مهامك</b>\n\n",
                "🎯 <b>您的任务</b>\n\n");

            var buttons = new List<InlineKeyboardButton[]>();

            foreach (var uq in userQuests)
            {
                var progressPercentage = uq.Quest.TargetValue > 0
                    ? Math.Min((uq.CurrentProgress / uq.Quest.TargetValue) * 100, 100)
                    : 0;

                var progressBar = GetProgressBar(progressPercentage, lang);
                var statusEmoji = uq.IsCompleted ? "✅" : "⏳";
                var rewardBadge = uq.IsRewarded ? "🎁" : "";

                var currentProgress = uq.CurrentProgress > uq.Quest.TargetValue
                    ? uq.Quest.TargetValue
                    : uq.CurrentProgress;

                // ФОРМАТУВАННЯ ЧИСЕЛ БЕЗ ЗАЙВИХ НУЛІВ
                string formattedCurrentProgress;
                string formattedTargetValue;
                string formattedReward = uq.Quest.Reward.ToString("F2"); // Форматування нагороди

                if (uq.Quest.ProgressUnit == "deposits" || uq.Quest.ProgressUnit == "investments" ||
                    uq.Quest.ProgressUnit == "friends" || uq.Quest.ProgressUnit == "times")
                {
                    // Цілі числа для підрахунку
                    formattedCurrentProgress = Math.Round(currentProgress, 0).ToString();
                    formattedTargetValue = Math.Round(uq.Quest.TargetValue, 0).ToString();
                }
                else if (uq.Quest.ProgressUnit == "USDT" || uq.Quest.ProgressUnit == "days")
                {
                    // Десяткові числа з 2 знаками після коми
                    formattedCurrentProgress = currentProgress.ToString("F2");
                    formattedTargetValue = uq.Quest.TargetValue.ToString("F2");
                }
                else
                {
                    // За замовчуванням
                    formattedCurrentProgress = currentProgress.ToString("F2");
                    formattedTargetValue = uq.Quest.TargetValue.ToString("F2");
                }

                                var progressText = $"{formattedCurrentProgress}/{formattedTargetValue} {GetLocalizedUnit(uq.Quest.ProgressUnit, lang)}";

                                message += T(lang,
                                        $"{statusEmoji} <b>{uq.Quest.Title}</b>\n📝 {uq.Quest.Description}\n{progressBar} ({progressPercentage:F0}%)\n📊 Progress: {progressText}\n💰 Reward: {formattedReward} USDT {rewardBadge}\n\n",
                                        $"{statusEmoji} <b>{uq.Quest.Title}</b>\n📝 {uq.Quest.Description}\n{progressBar} ({progressPercentage:F0}%)\n📊 Progress: {progressText}\n💰 Reward: {formattedReward} USDT {rewardBadge}\n\n",
                                        $"{statusEmoji} <b>{uq.Quest.Title}</b>\n📝 {uq.Quest.Description}\n{progressBar} ({progressPercentage:F0}%)\n📊 Прогресс: {progressText}\n💰 Награда: {formattedReward} USDT {rewardBadge}\n\n",
                                        $"{statusEmoji} <b>{uq.Quest.Title}</b>\n📝 {uq.Quest.Description}\n{progressBar} ({progressPercentage:F0}%)\n📊 پیشرفت: {progressText}\n💰 پاداش: {formattedReward} USDT {rewardBadge}\n\n",
                                        $"{statusEmoji} <b>{uq.Quest.Title}</b>\n📝 {uq.Quest.Description}\n{progressBar} ({progressPercentage:F0}%)\n📊 التقدم: {progressText}\n💰 المكافأة: {formattedReward} USDT {rewardBadge}\n\n",
                                        $"{statusEmoji} <b>{uq.Quest.Title}</b>\n📝 {uq.Quest.Description}\n{progressBar} ({progressPercentage:F0}%)\n📊 进度：{progressText}\n💰 奖励：{formattedReward} USDT {rewardBadge}\n\n");

                if (uq.IsCompleted && !uq.IsRewarded)
                {
                    buttons.Add(new[]
                    {
                        InlineKeyboardButton.WithCallbackData(
                            T(lang, $"🎁 Claim {formattedReward} USDT", $"🎁 {formattedReward} USDT Claim Karo", $"🎁 Получить {formattedReward} USDT", $"🎁 دریافت {formattedReward} USDT", $"🎁 المطالبة بـ {formattedReward} USDT", $"🎁 领取 {formattedReward} USDT"),
                            $"claim_quest_{uq.QuestId}")
                    });
                }
                else if (!uq.IsCompleted && uq.Quest.TargetType == "manual")
                {
                    buttons.Add(new[]
                    {
                        InlineKeyboardButton.WithCallbackData(
                            T(lang, "⚡ Complete", "⚡ Complete Karo", "⚡ Выполнить", "⚡ تکمیل", "⚡ إكمال", "⚡ 完成"),
                            $"complete_quest_{uq.QuestId}")
                    });
                }
            }

            // Кнопка оновлення прогресу
            buttons.Add(new[]
            {
                InlineKeyboardButton.WithCallbackData(
                    T(lang, "🔄 Update Progress", "🔄 Progress Update Karo", "🔄 Обновить прогресс", "🔄 به روزرسانی پیشرفت", "🔄 تحديث التقدم", "🔄 更新进度"),
                    "update_quests_progress")
            });

            // Кнопка "Back to Menu"
            buttons.Add(new[]
            {
                InlineKeyboardButton.WithCallbackData(
                    T(lang, "🔙 Back to Menu", "🔙 Wapas Menu", "🔙 Назад в меню", "🔙 بازگشت به منو", "🔙 العودة إلى القائمة", "🔙 返回菜单"),
                    "back_to_menu")
            });

            var markup = new InlineKeyboardMarkup(buttons.ToArray());

            try
            {
                await _botClient.EditMessageText(
                    chatId: callback.Message.Chat.Id,
                    messageId: callback.Message.MessageId,
                    text: message,
                    parseMode: ParseMode.Html,
                    replyMarkup: markup);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Could not edit message: {ex.Message}");
                await _botClient.SendMessage(
                    callback.Message.Chat.Id,
                    message,
                    parseMode: ParseMode.Html,
                    replyMarkup: markup);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error in HandleQuestCallback: {ex.Message}");

            var errorKeyboard = new InlineKeyboardMarkup(new[]
            {
                new[]
                {
                    InlineKeyboardButton.WithCallbackData("🔙 Back to Menu", "back_to_menu")
                }
            });

            await _botClient.EditMessageText(
                chatId: callback.Message.Chat.Id,
                messageId: callback.Message.MessageId,
                text: "❌ <b>Error loading quests</b>\n\nPlease try again later.",
                parseMode: ParseMode.Html,
                replyMarkup: errorKeyboard);
        }
    }
}