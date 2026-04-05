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
        IDelayCompensationService delayCompensationService)
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


            switch (data)
            {
                // =================== Вибір мови ===================
                case "lang_en":
                    await _manageService.UpdateUserLanguageAsync(callbackChatId, "English");
                    await bot.AnswerCallbackQuery(callback.Id, "✅ Language saved");
                    await ShowAuthOptions(callbackChatId, "English"); // Одразу показуємо реєстрацію/вхід
                    break;

                case "lang_hinglish":
                    await _manageService.UpdateUserLanguageAsync(callbackChatId, "Hinglish");
                    await bot.AnswerCallbackQuery(callback.Id, "✅ Bhasha save ki gayi");
                    await ShowAuthOptions(callbackChatId, "Hinglish"); // Одразу показуємо реєстрацію/вхід
                    break;

                case "start_auth":
                    var userLang = await _manageService.GetUserLanguageAsync(callbackChatId);
                    var authButtons = new InlineKeyboardMarkup(new[]
                    {
                        new[]
                        {
                            InlineKeyboardButton.WithCallbackData(
                                userLang == "English" ? "📝 Register" : "📝 Register",
                                "register"),
                            InlineKeyboardButton.WithCallbackData(
                                userLang == "English" ? "🔑 Login" : "🔑 Login",
                                "login")
                        }
                    });
                    await bot.SendMessage(callbackChatId,
                        userLang == "English" ? "Choose an action:" : "Action chuno:",
                        replyMarkup: authButtons);
                    break;

                case "register":
                    await ShowRegistrationRulesAsync(callbackChatId,
                        await _manageService.GetUserLanguageAsync(callbackChatId));
                    break;

                case "login":
                    await _userStateService.SetStateAsync(callbackChatId, UserAction.WaitingLoginPassword);
                    await bot.SendMessage(callbackChatId,
                        await _manageService.GetUserLanguageAsync(callbackChatId) == "English"
                            ? "🔑 Enter your password to login:"
                            : "🔑 Login karne ke liye password dalo:");
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
                        currentLang == "English"
                            ? "✏️ Please enter your desired username:"
                            : "✏️ Apna username likho:");
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
                    var readUserLang = readUser.Language;
                    string confirmation = readUserLang == "English"
                        ? "👍 Let's get started!"
                        : "👍 Chalo shuru karein!";

                    await bot.SendMessage(callbackChatId, confirmation);

                    // Показуємо головне меню
                    await ShowMainMenuAsync(callbackChatId);

                    // Підтверджуємо натискання кнопки
                    await bot.AnswerCallbackQuery(callback.Id);
                    break;
                case "about":
                    var aboutUser = await _manageService.GetUserByTelegramIdAsync(callbackChatId);
                    var aboutLang = aboutUser?.Language ?? "English";
                    bool isEnglishAbout = aboutLang == "English";

                    var aboutText = isEnglishAbout
                        ? "🌟 *SHANTI AI TRADING PLATFORM* 🌟\n\n" +
                          "🤖 *Advanced AI Trading Technology*\n" +
                          "ShantiAI is a sophisticated algorithmic trading system designed for consistent and secure capital growth.\n\n" +
                          "🚀 *How Our System Works*\n\n" +
                          "💳 *1. Deposit Funds*\n" +
                          "• Transfer USDT to our secure Trust Wallet\n" +
                          "• Minimum investment: 1 USDT\n" +
                          "• TRC20 network only\n\n" +
                          "📊 *2. AI Trading Execution*\n" +
                          "• Advanced algorithms analyze multiple markets\n" +
                          "• Diversified investment strategies\n" +
                          "• Real-time market monitoring\n\n" +
                          "📈 *3. Automated Growth*\n" +
                          "• Fully automated trading process\n" +
                          "• Daily profit accumulation\n" +
                          "• Transparent performance tracking\n\n" +
                          "🛡️ *Security Features*\n\n" +
                          "🔐 *Proprietary Technology*\n" +
                          "• Unique AI algorithm cannot be replicated\n" +
                          "• Advanced risk management systems\n\n" +
                          "💎 *Funds Protection*\n" +
                          "• Secure from wallet freezes\n" +
                          "• No third-party control\n" +
                          "• No KYC requirements\n\n" +
                          "⚡ *256-bit Encryption*\n" +
                          "• Military-grade security protocols\n" +
                          "• Regular security audits\n\n" +
                          "🎯 *Our Investment Philosophy*\n\n" +
                          "📊 *Steady Growth Focus*\n" +
                          "• Consistent returns over time\n" +
                          "• Minimal risk exposure\n" +
                          "• No risky pump trades\n\n" +
                          "🌱 *Long-Term Strategy*\n" +
                          "• Sustainable profit generation\n" +
                          "• Diversified portfolio approach\n" +
                          "• Continuous algorithm optimization"
                        : "🌟 *SHANTI AI TRADING PLATFORM* 🌟\n\n" +
                          "🤖 *Advanced AI Trading Technology*\n" +
                          "ShantiAI ek advanced algorithmic trading system hai jo consistent aur secure capital growth ke liye design kiya gaya hai.\n\n" +
                          "🚀 *Hamara System Kaise Kaam Karta Hai*\n\n" +
                          "💳 *1. Funds Deposit Karein*\n" +
                          "• Hamare secure Trust Wallet mein USDT transfer karein\n" +
                          "• Minimum investment: 1 USDT\n" +
                          "• Sirf TRC20 network\n\n" +
                          "📊 *2. AI Trading Execution*\n" +
                          "• Advanced algorithms multiple markets ka analysis karte hain\n" +
                          "• Diversified investment strategies\n" +
                          "• Real-time market monitoring\n\n" +
                          "📈 *3. Automated Growth*\n" +
                          "• Fully automated trading process\n" +
                          "• Daily profit accumulation\n" +
                          "• Transparent performance tracking\n\n" +
                          "🛡️ *Security Features*\n\n" +
                          "🔐 *Proprietary Technology*\n" +
                          "• Unique AI algorithm copy nahi ho sakta\n" +
                          "• Advanced risk management systems\n\n" +
                          "💎 *Funds Protection*\n" +
                          "• Wallet freezes se secure\n" +
                          "• Third-party control nahi\n" +
                          "• KYC requirements nahi\n\n" +
                          "⚡ *256-bit Encryption*\n" +
                          "• Military-grade security protocols\n" +
                          "• Regular security audits\n\n" +
                          "🎯 *Hamari Investment Philosophy*\n\n" +
                          "📊 *Steady Growth Focus*\n" +
                          "• Consistent returns over time\n" +
                          "• Minimal risk exposure\n" +
                          "• Risky pump trades nahi\n\n" +
                          "🌱 *Long-Term Strategy*\n" +
                          "• Sustainable profit generation\n" +
                          "• Diversified portfolio approach\n" +
                          "• Continuous algorithm optimization";

                    var aboutKeyboard = new InlineKeyboardMarkup(new[]
                    {
                        new[]
                        {
                            InlineKeyboardButton.WithCallbackData(
                                isEnglishAbout ? "🔙 Back to Menu" : "🔙 Wapas Menu",
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
                    var refLang = callbackUser?.Language ?? "English";

                    if (callbackUser == null)
                    {
                        string notFoundMessage = refLang == "English"
                            ? "❌ <b>User Not Found</b>\n\nPlease complete your registration to access the referral program. 🚀"
                            : "❌ <b>User Nahi Mila</b>\n\nReferral program ka istemal karne ke liye, pehle apna registration poora karein. 🚀";

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

                    if (refLang == "English")
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
                                    $"tg://msg_url?url={Uri.EscapeDataString(referralLink)}&text={Uri.EscapeDataString("Join me on ShantiAI - AI-powered investments! 🚀")}")
                            },
                            new[]
                            {
                                InlineKeyboardButton.WithCallbackData("🔙 Back to Menu", "back_to_menu")
                            }
                        });
                    }
                    else // Hinglish
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
                                    $"tg://msg_url?url={Uri.EscapeDataString(referralLink)}&text={Uri.EscapeDataString("ShantiAI mein mere saath join karo - AI-powered investments! 🚀")}")
                            },
                            new[]
                            {
                                InlineKeyboardButton.WithCallbackData("🔙 Wapas Menu me", "back_to_menu")
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
                        var profLang = myProfUser?.Language ?? "English";

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
                                var language = profLang == "English" ? "en" : "uk";
                                var img = await _chartRendererService.RenderTradingChartAsync(candles, metrics,
                                    language);

                                await using var ms = new System.IO.MemoryStream(img);

// Формуємо підпис з інформацією про прибуток
                                var chartCaption = profLang == "English"
                                    ? $"📊 <b>Investment Performance Chart</b>\n\n" +
                                      $"📈 <b>Real-Time Profit Tracking</b>\n\n" +
                                      $"💰 <b>Current Price:</b> ${metrics.CurrentPrice:F2}\n" +
                                      $"💎 <b>Profit:</b> ${Math.Abs(metrics.ProfitLoss):F2}\n\n" +
                                      $"💡 <i>Green candles = Profit growth | Red candles = Market fluctuation</i>"
                                    : $"📊 <b>Investment Performance Chart</b>\n\n" +
                                      $"📈 <b>Real-Time Profit Tracking</b>\n\n" +
                                      $"💰 <b>Current Price:</b> ${metrics.CurrentPrice:F2}\n" +
                                      $"📊 <b>Price Change:</b> {(metrics.ChangePercent >= 0 ? "+" : "")}{metrics.ChangePercent:F2}%\n" +
                                      $"💹 <b>Balance Impact:</b> {(metrics.BalanceChangePercent >= 0 ? "+" : "")}{metrics.BalanceChangePercent:F2}%\n\n" +
                                      $"💎 <b>Profit/Loss:</b> ${Math.Abs(metrics.ProfitLoss):F2}\n\n" +
                                      $"💡 <i>Zelene Mombattiyaan = Profit | Laal Mombattiyaan = Fluctuation</i>";

// ✅ Додаємо кнопки під графік
                                var chartButtons = new InlineKeyboardMarkup(new[]
                                {
                                    new[]
                                    {
                                        InlineKeyboardButton.WithCallbackData(
                                            profLang == "English" ? "🔙 Back to Menu" : "🔙 Wapas Menu",
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
                                var fallbackCaption = profLang == "English"
                                    ? $"📊 <b>Investment Status</b>\n" +
                                      $"💰 Price: ${metrics.CurrentPrice:F2}\n" +
                                      $"📈 Change: {metrics.ChangePercent:F2}%\n" +
                                      $""
                                    : $"📊 <b>Investment Status</b>\n" +
                                      $"💰 Price: ${metrics.CurrentPrice:F2}\n" +
                                      $"📈 Change: {metrics.ChangePercent:F2}%\n";

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
                    var supportLang = supportUser?.Language ?? "English";
                    bool isEnglishSupport = supportLang == "English";

                    var supportText = isEnglishSupport
                        ? "🎯 *SUPPORT CENTER*\n\n" +
                          "🛟 *Need Assistance?*\n" +
                          "Our team is here to help you succeed!\n\n" +
                          "📚 *Quick Start Guide*\n\n" +
                          "💰 *1. Make a Deposit*\n" +
                          "• Send USDT (TRC20 network only)\n" +
                          "• Minimum: 1 USDT\n" +
                          "• Confirm your transaction\n\n" +
                          "⏳ *2. Wait for Confirmation*\n" +
                          "• Usually takes 15-45 minutes\n" +
                          "• Funds will appear in your profile\n\n" +
                          "📊 *3. Track Your Portfolio*\n" +
                          "• Monitor investments in real-time\n" +
                          "• View projected earnings\n" +
                          "• Check referral rewards\n\n" +
                          "🚀 *4. Start Investing*\n" +
                          "• Choose investment amount\n" +
                          "• Select duration (2-14 days)\n" +
                          "• Earn daily profits\n\n" +
                          "📞 *Contact Support*\n\n" +
                          "💬 *Telegram:* @ShantiAIWE\n" +
                          "📧 *Email:* support@shanti.ai\n" +
                          "⏰ *Response Time:* < 24 hours\n\n" +
                          "🔒 *Security Guarantee*\n" +
                          "• Your funds remain under your control\n" +
                          "• We never access your wallet directly\n" +
                          "• All transactions are transparent\n" +
                          "• 256-bit encryption protection"
                        : "🎯 *SUPPORT CENTER*\n\n" +
                          "🛟 *Madad Chahiye?*\n" +
                          "Hamari team aapki madad ke liye yaha hai!\n\n" +
                          "📚 *Quick Start Guide*\n\n" +
                          "💰 *1. Deposit Karein*\n" +
                          "• USDT bhejein (sirf TRC20 network)\n" +
                          "• Minimum: 1 USDT\n" +
                          "• Apna transaction confirm karein\n\n" +
                          "⏳ *2. Confirmation Ka Intezar Karein*\n" +
                          "• Aam taur par 15-45 minutes lagte hain\n" +
                          "• Funds aapke profile mein dikhenge\n\n" +
                          "📊 *3. Apna Portfolio Track Karein*\n" +
                          "• Real-time mein investments dekhein\n" +
                          "• Projected earnings check karein\n" +
                          "• Referral rewards dekhein\n\n" +
                          "🚀 *4. Investing Shuru Karein*\n" +
                          "• Investment amount chunein\n" +
                          "• Duration select karein (2-14 days)\n" +
                          "• Daily profit kamayein\n\n" +
                          "📞 *Support Se Contact Karein*\n\n" +
                          "💬 *Telegram:* @ShantiAIWE\n" +
                          "📧 *Email:* support@shanti.ai\n" +
                          "⏰ *Response Time:* < 24 hours\n\n" +
                          "🔒 *Security Guarantee*\n" +
                          "• Aapke funds aapke control mein rahte hain\n" +
                          "• Hum kabhi bhi aapka wallet access nahi karte\n" +
                          "• Sabhi transactions transparent hain\n" +
                          "• 256-bit encryption protection";

                    var supportKeyboard2 = new InlineKeyboardMarkup(new[]
                    {
                        new[]
                        {
                            InlineKeyboardButton.WithUrl(
                                isEnglishSupport ? "💬 Contact Support" : "💬 Support Se Contact",
                                "https://t.me/ShantiAIWE")
                        },
                        new[]
                        {
                            InlineKeyboardButton.WithCallbackData(
                                isEnglishSupport ? "🔙 Back to Menu" : "🔙 Wapas Menu",
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
                    var depositLang = depositUser?.Language ?? "English";

                    // Перевірка авторизації
                    if (depositUser == null || !depositUser.IsAuthorized)
                    {
                        string authMessage = depositLang == "English"
                            ? "🔐 <b>Registration Required</b>\n\nTo make a deposit, please complete your registration first. It's quick and easy! 🚀"
                            : "🔐 <b>Registration Zaroori Hai</b>\n\nDeposit karne ke liye, pehle apna registration poora karein. Yeh jaldi aur aasan hai! 🚀";

                        await _botClient.SendMessage(
                            chatId: callbackChatId,
                            text: authMessage,
                            parseMode: ParseMode.Html
                        );
                        return;
                    }

                    await _userStateService.SetStateAsync(callbackChatId, UserAction.WaitingDepositAmount);

                    // Професійне та дружнє повідомлення про депозит
                    string depositMessage = depositLang == "English"
                        ? "💰 <b>Make a Deposit</b>\n\n✨ <i>Ready to grow your investment?</i>\n\nPlease enter the amount you'd like to deposit:\n\n• <b>Minimum:</b> 1 USDT\n• <b>Network:</b> TRC20 (TRON)\n• <b>Currency:</b> USDT only\n\n❓ <i>Need help? Use the button below.</i>"
                        : "💰 <b>Deposit Karein</b>\n\n✨ <i>Apne nivesh ko badhane ke liye taiyar?</i>\n\nKripya woh raash daalen jise aap deposit karna chahte hain:\n\n• <b>Minimum:</b> 1 USDT\n• <b>Network:</b> TRC20 (TRON)\n• <b>Currency:</b> Sirf USDT\n\n❓ <i>Madad chahiye? Neeche button use karein.</i>";

                    // Створюємо клавіатуру з кнопкою підтримки
                    var supportKeyboard = new InlineKeyboardMarkup(new[]
                    {
                        new[]
                        {
                            InlineKeyboardButton.WithCallbackData(
                                depositLang == "English" ? "🛟 Contact Support" : "🛟 Support Se Sampark Karein",
                                "support_help")
                        },
                        new[]
                        {
                            InlineKeyboardButton.WithCallbackData(
                                depositLang == "English" ? "🔙 Back to Menu" : "🔙 Wapas Menu",
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
                    var confirmLang = confirmUser?.Language ?? "English";

                    if (_pendingDeposits.TryGetValue(callbackChatId, out var depositAmount))
                    {
                        await _operationService.CreateDepositRequestAsync(callbackChatId, depositAmount);
                        _pendingDeposits.Remove(callbackChatId);

                        // Language-specific messages
                        var (successMessage, backButtonText) = confirmLang == "English"
                            ? ($"🎉 <b>Deposit Successful!</b>\n\n" +
                               $"✅ <b>Amount:</b> {depositAmount} USDT\n\n" +
                               "⏳ <b>Status:</b> Processing...\n" +
                               "Your deposit request has been received and is being processed.\n\n" +
                               "📋 <b>What happens next?</b>\n" +
                               "• We'll verify the transaction\n" +
                               "• Funds will be added to your balance\n" +
                               "• You'll receive a confirmation message\n\n" +
                               "⏰ <i>Usually takes 15-45 minutes</i>\n\n" +
                               "Thank you for choosing ShantiAI! 💙",
                                "🏠 Back to Menu")
                            : ($"🎉 <b>Deposit Safal!</b>\n\n" +
                               $"✅ <b>Raash:</b> {depositAmount} USDT\n\n" +
                               "⏳ <b>Status:</b> Processing...\n" +
                               "Aapka deposit request receive ho gaya hai aur process ho raha hai.\n\n" +
                               "📋 <b>Aage kya hoga?</b>\n" +
                               "• Hum transaction verify karenge\n" +
                               "• Funds aapke balance mein add honge\n" +
                               "• Aapko confirmation message milega\n\n" +
                               "⏰ <i>Aam taur par 15-45 minute lagte hain</i>\n\n" +
                               "ShantiAI choose karne ke liye dhanyavaad! 💙",
                                "🏠 Wapas Menu");

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
                        string errorMessage = confirmLang == "English"
                            ? "⚠️ <b>Deposit Not Found</b>\n\nWe couldn't find your pending deposit. Please try making a deposit again or contact support if the issue persists."
                            : "⚠️ <b>Deposit Nahi Mila</b>\n\nHum aapka pending deposit nahi dhundh paaye. Kripya phir se deposit karne ka prayas karein ya agar problem bani rahe to support se sampark karein.";

                        var errorButtons = new InlineKeyboardMarkup(new[]
                        {
                            new[]
                            {
                                InlineKeyboardButton.WithCallbackData("💳 Try Deposit Again", "deposit"),
                                InlineKeyboardButton.WithCallbackData("🛟 Contact Support", "support_help")
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
                    var approveMessage = request.User.Language == "English"
                        ? $"✅ Your deposit request for {request.Amount} USDT has been approved. Your balance was updated."
                        : $"✅ Aapka {request.Amount} USDT deposit request approve ho gaya. Aapka balance update kar diya gaya.";

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
                    var callbackLang = withdrawUser?.Language ?? "English";

                    if (withdrawUser == null || !withdrawUser.IsAuthorized)
                    {
                        string authMessage = callbackLang == "English"
                            ? "🔐 <b>Registration Required</b>\n\nTo make a withdrawal, please complete your registration first! 🚀"
                            : "🔐 <b>Registration Zaroori Hai</b>\n\nWithdrawal karne ke liye, pehle apna registration poora karein! 🚀";

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
                        string balanceMessage = callbackLang == "English"
                            ? $"⚠️ <b>Insufficient Balance</b>\n\n💰 <b>Current Balance:</b> {withdrawUser.Balance:N2} USDT\n📉 <b>Minimum Withdrawal:</b> {minWithdraw} USDT\n\nPlease deposit more funds to make a withdrawal. 💳"
                            : $"⚠️ <b>Paryapt Balance Nahi</b>\n\n💰 <b>Current Balance:</b> {withdrawUser.Balance:N2} USDT\n📉 <b>Minimum Withdrawal:</b> {minWithdraw} USDT\n\nWithdrawal karne ke liye, kripya aur funds deposit karein. 💳";

                        await _botClient.SendMessage(
                            chatId: callbackChatId,
                            text: balanceMessage,
                            parseMode: ParseMode.Html
                        );
                        await bot.AnswerCallbackQuery(callback.Id);
                        return;
                    }

                    await _userStateService.SetStateAsync(callbackChatId, UserAction.WaitingWithdrawAmount);

                    string messageText = callbackLang == "English"
                        ? "💳 <b>Withdraw Funds</b>\n\n" +
                          "✨ <i>Ready to transfer your earnings?</i>\n\n" +
                          $"💰 <b>Available Balance:</b> {withdrawUser.Balance:N2} USDT\n" +
                          $"📉 <b>Minimum Withdrawal:</b> {minWithdraw} USDT\n\n" +
                          "🌐 <b>Network:</b> TRC20 (TRON)\n" +
                          "💵 <b>Currency:</b> USDT only\n" +
                          "⏰ <b>Processing Time:</b> Up to 12 hours\n\n" +
                          "↳ <b>Please enter the amount you want to withdraw:</b>"
                        : "💳 <b>Funds Nikale</b>\n\n" +
                          "✨ <i>Apni earnings transfer karne ke liye taiyar?</i>\n\n" +
                          $"💰 <b>Upalabdh Balance:</b> {withdrawUser.Balance:N2} USDT\n" +
                          $"📉 <b>Minimum Withdrawal:</b> {minWithdraw} USDT\n\n" +
                          "🌐 <b>Network:</b> TRC20 (TRON)\n" +
                          "💵 <b>Currency:</b> Sirf USDT\n" +
                          "⏰ <b>Processing Time:</b> 12 ghante tak\n\n" +
                          "↳ <b>Kripya woh raash daalen jise aap withdraw karna chahte hain:</b>";

                    // Створюємо клавіатуру з кнопкою Cancel
                    var cancelKeyboard = new InlineKeyboardMarkup(new[]
                    {
                        new[]
                        {
                            InlineKeyboardButton.WithCallbackData(
                                callbackLang == "English" ? "❌ Cancel" : "❌ Cancel",
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

                    await _settingsService.ShowSettingsMenuAsync(callbackChatId, settingUser.Language, callbackMessageId);
                    break;


                case "change_language":
                    var changeLangUser = await _manageService.GetUserByTelegramIdAsync(callbackChatId);

                    await _settingsService.ShowLanguageSelectionAsync(callbackChatId, changeLangUser.Language, callbackMessageId);
                    break;

                case "set_language_english":
                    var setLangUser = await _manageService.GetUserByTelegramIdAsync(callbackChatId);
                    await _settingsService.UpdateUserLanguageAsync(callbackChatId, "English");
                    // Оновлюємо мову в поточному об'єкті користувача
                    setLangUser.Language = "English";
                    await ShowMainMenuAsync(callbackChatId);
                    break;

                case "set_language_hindi":
                    var hindiUser = await _manageService.GetUserByTelegramIdAsync(callbackChatId);
                    await _settingsService.UpdateUserLanguageAsync(callbackChatId, "Hindi");
                    // Оновлюємо мову в поточному об'єкті користувача
                    hindiUser.Language = "Hindi";
                    await ShowMainMenuAsync(callbackChatId);
                    break;

                case "change_login":
                    var loginUser = await _manageService.GetUserByTelegramIdAsync(callbackChatId);
                    await _settingsService.AskForNewLoginAsync(callbackChatId, loginUser.Language);
                    // Встановлюємо стан очікування нового логіну
                    await _userStateService.SetStateAsync(callbackChatId, UserAction.WaitingForNewLogin);
                    break;

                case "change_password":
                    var passwordUser = await _manageService.GetUserByTelegramIdAsync(callbackChatId);
                    await _settingsService.AskForNewPasswordAsync(callbackChatId, passwordUser.Language);
                    // Встановлюємо стан очікування нового пароля
                    await _userStateService.SetStateAsync(callbackChatId, UserAction.WaitingForNewPassword);
                    break;

                case "back_to_settings":
                    var backToSettingsUser = await _manageService.GetUserByTelegramIdAsync(callbackChatId);
                    await _settingsService.ShowSettingsMenuAsync(callbackChatId, backToSettingsUser.Language, callbackMessageId);
                    break;
                case "change_wallet":
                    try
                    {
                        // Отримуємо дані користувача
                        var walletUser = await _manageService.GetUserByTelegramIdAsync(callbackChatId);
                        if (walletUser == null)
                        {
                            _logger.LogWarning("User not found for chat ID: {ChatId}", callbackChatId);
                            await _botClient.AnswerCallbackQuery(callback.Id,
                                "❌ User configuration error");
                            return;
                        }

                        // Відправляємо запит на нову адресу гаманця
                        await _settingsService.AskForNewWalletAddressAsync(callbackChatId, walletUser.Language);

                        // Встановлюємо стан очікування введення даних
                        await _userStateService.SetStateAsync(callbackChatId, UserAction.WaitingForNewWalletAddress);

                        // Підтверджуємо обробку запиту
                        bool isEnglish = walletUser.Language == "English";
                        await _botClient.AnswerCallbackQuery(
                            callback.Id,
                            isEnglish ? "📍 Enter your new wallet address" : "📍 Apna naya wallet address enter karen"
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
                    var investLang = investUser?.Language ?? "English";

                    // Перевірка авторизації
                    if (investUser == null || !investUser.IsAuthorized)
                    {
                        string authMessage = investLang == "English"
                            ? "🔐 <b>Registration Required</b>\n\nTo start investing, please complete your registration first! 🚀"
                            : "🔐 <b>Registration Zaroori Hai</b>\n\nInvest shuru karne ke liye, pehle apna registration poora karein! 🚀";

                        await _botClient.SendMessage(
                            chatId: callbackChatId,
                            text: authMessage,
                            parseMode: ParseMode.Html
                        );
                        return;
                    }

                    await _userStateService.SetStateAsync(callbackChatId, UserAction.WaitingInvestDuration);

                    var durationKeyboard = investLang == "English"
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

                    var messageText = investLang == "English"
                        ? "💰 <b>Choose Investment Plan</b>\n\n✨ <i>Select the duration that suits your goals:</i>\n\n• <b>Short-term</b> - Quick returns\n• <b>Medium-term</b> - Balanced growth  \n• <b>Long-term</b> - Maximum profit\n\n⏳ <b>Please select your preferred duration:</b>"
                        : "💰 <b>Investment Plan Chuno</b>\n\n✨ <i>Apne targets ke hisaab se time chuno:</i>\n\n• <b>Short-term</b> - Jaldi returns\n• <b>Medium-term</b> - Balanced growth  \n• <b>Long-term</b> - Zyada profit\n\n⏳ <b>Apna preferred duration chuno:</b>";

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
                    var durFourLang = durationFortyUser?.Language ?? "English";

                    string messageFortyDur = durFourLang == "English"
                        ? "💎 <b>2-Day Investment</b>\n\n📈 <i>Expected ROI: 1% - 3%</i>\n\n💸 <b>Enter investment amount:</b>\n\n• Minimum: 1 USDT\n• Maximum: No limit\n\n↳ Please type the amount:"
                        : "💎 <b>2-Din Investment</b>\n\n📈 <i>Expected ROI: 1% - 3%</i>\n\n💸 <b>Investment raash daalo:</b>\n\n• Minimum: 1 USDT\n• Maximum: No limit\n\n↳ Kripya raash type karein:";

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
                    var durLang = durationOneUser?.Language ?? "English";

                    string messageDur = durLang == "English"
                        ? "💎 <b>1-Week Investment</b>\n\n📈 <i>Expected ROI: 8% - 13%</i>\n\n💸 <b>Enter investment amount:</b>\n\n• Minimum: 1 USDT\n• Maximum: No limit\n\n↳ Please type the amount:"
                        : "💎 <b>1-Hafta Investment</b>\n\n📈 <i>Expected ROI: 8% - 13%</i>\n\n💸 <b>Investment raash daalo:</b>\n\n• Minimum: 1 USDT\n• Maximum: No limit\n\n↳ Kripya raash type karein:";

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
                    var durTwoLang = durationTwoUser?.Language ?? "English";

                    string messageDurTwo = durTwoLang == "English"
                        ? "💎 <b>2-Week Investment</b>\n\n📈 <i>Expected ROI: 22% - 30%</i>\n\n💸 <b>Enter investment amount:</b>\n\n• Minimum: 1 USDT\n• Maximum: No limit\n\n↳ Please type the amount:"
                        : "💎 <b>2-Hafte Investment</b>\n\n📈 <i>Expected ROI: 22% - 30%</i>\n\n💸 <b>Investment raash daalo:</b>\n\n• Minimum: 1 USDT\n• Maximum: No limit\n\n↳ Kripya raash type karein:";

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
                        var langProgress = userProgress?.Language ?? "English";

                        await _botClient.AnswerCallbackQuery(callback.Id,
                            langProgress == "English"
                                ? "🔄 Progress updated automatically!"
                                : "🔄 Прогрес оновлено автоматично!",
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
                            var langData = userDataQuest?.Language ?? "English";

                            if (success)
                            {
                                await _botClient.AnswerCallbackQuery(callback.Id,
                                    langData == "English"
                                        ? "✅ Progress updated!"
                                        : "✅ Прогрес оновлено!",
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
                            var langClaim = userClaim?.Language ?? "English";

                            if (success)
                            {
                                var rewardMsg = langClaim == "English"
                                    ? $"🎉 Quest completed!\n\n💰 You received {reward} USDT"
                                    : $"🎉 Квест виконано!\n\n💰 Ви отримали {reward} USDT";

                                await _botClient.AnswerCallbackQuery(callback.Id, rewardMsg, showAlert: true);

                                // Оновлюємо повідомлення після отримання нагороди
                                await HandleQuestCallback(callback);
                            }
                            else
                            {
                                var errorMsg = langClaim == "English"
                                    ? "❌ Quest already claimed or not completed"
                                    : "❌ Нагороду вже отримано або квест не виконано";

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
        var lang = user?.Language ?? "English";

        // ================= WaitingRulesAccept =================
        // =================== WaitingRulesAccept ===================
        if (state == UserAction.WaitingRulesAccept)
        {
            await bot.SendMessage(chatId,
                lang == "English"
                    ? "❗ Please use the ✅ button to confirm that you have read the rules."
                    : "❗ Kripya rules ko confirm karne ke liye ✅ button dabayein.");
            return;
        }

        // =================== Registration ===================

        // ------------------- Реєстрація -------------------

        if (state == UserAction.WaitingRegisterUsername)
        {
            _pendingUsernames[chatId] = msgText;
            await _userStateService.SetStateAsync(chatId, UserAction.WaitingRegisterPassword);

            await bot.SendMessage(chatId,
                user.Language == "English"
                    ? "🔑 Enter your password (at least 6 characters, letters + numbers):"
                    : "🔑 Password daalo (kam se kam 6 characters, letters + numbers):");
            return;
        }

        if (state == UserAction.WaitingRegisterPassword)
        {
            string regPassword = msgText.Trim();

            // 🔎 Перевірка довжини пароля
            if (regPassword.Length < 6)
            {
                await bot.SendMessage(chatId,
                    user.Language == "English"
                        ? "❌ Password too short. Please enter at least 6 characters (letters + numbers):"
                        : "❌ Password bahut chhota hai. Kam se kam 6 characters daalo (letters + numbers):");
                await _userStateService.SetStateAsync(chatId, UserAction.WaitingRegisterPassword);
                return;
            }

            _pendingPasswords[chatId] = regPassword;
            await _userStateService.SetStateAsync(chatId, UserAction.WaitingRegisterWallet);

            await bot.SendMessage(chatId,
                user.Language == "English"
                    ? "💼 <b>Enter your TRC20 wallet address:</b>\n\n" +
                      "ℹ️ <i>This is your wallet from which deposits and withdrawals will be processed.</i>\n" +
                      "⚠️ <i>Wallet address must be at least 32 characters long</i>"
                    : "💼 <b>Apna TRC20 wallet address daalo:</b>\n\n" +
                      "ℹ️ <i>Ye aapka wallet hoga jisme se deposit aur withdrawal hoga.</i>\n" +
                      "⚠️ <i>Wallet address kam se kam 32 characters lamba hona chahiye</i>",
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
                    user.Language == "English"
                        ? "❌ Wallet address too short. Please enter a valid USDT wallet (at least 32 characters):\n\n" +
                          "ℹ️ <i>This is your wallet from which deposits and withdrawals will be processed.</i>"
                        : "❌ Wallet address bahut chhota hai. Sahi USDT wallet daalo (kam se kam 32 characters):\n\n" +
                          "ℹ️ <i>Ye aapka wallet hoga jisme se deposit aur withdrawal hoga.</i>",
                    parseMode: ParseMode.Html);

                await _userStateService.SetStateAsync(chatId, UserAction.WaitingRegisterWallet);
                return;
            }

            var result =
                await _registerService.RegisterUserAsync(chatId, regUsername, regPassword, walletAddress,
                    user.Language, regReferralCode);

            if (registeringUser != null)
            {
                registeringUser.PendingReferralCode = null;
                await _botDbContext.SaveChangesAsync();
            }

            await bot.SendMessage(chatId, result.Message);

            // --- ПОЧАТОК: Нова інструкція після реєстрації ---
            if (result.Success) // Надсилаємо інструкцію тільки якщо реєстрація була успішною
            {
                string instructions = user.Language == "English"
                    ? "🎉 <b>Registration Successful!</b>\n\n" +
                      "📖 <b>Quick Start Guide:</b>\n\n" +
                      "1️⃣ <b>Make a Deposit</b>\n" +
                      " • Enter the amount in USDT (TRC20) and confirm.\n\n" +
                      "2️⃣ <b>Wait for Confirmation</b>\n" +
                      " • Once the transaction is confirmed, your deposit will appear in your profile.\n\n" +
                      "3️⃣ <b>Track Your Balance</b>\n" +
                      " • In the My Profile section you will see your deposit, active investments, and profit.\n\n" +
                      "4️⃣ <b>Start Investing</b>\n" +
                      " • To activate AI trading, press the Invest button and choose the period.\n\n" +
                      "🔒 <b>Important</b>\n" +
                      " • All funds are securely linked to your wallet.\n" +
                      " • Withdrawals are possible only to the same wallet used for deposit.\n" +
                      " • As this is beta testing, small delays may occur."
                    : "🎉 <b>Registration Safal Ho Gayi!</b>\n\n" +
                      "📖 <b>Jaldi Start Guide:</b>\n\n" +
                      "1️⃣ <b>Deposit Karo</b>\n" +
                      " • USDT (TRC20) mein raashi daalen aur confirm karen.\n\n" +
                      "2️⃣ <b>Confirmation Ka Intezar Karo</b>\n" +
                      " • Transaction confirm hone ke baad, aapka deposit aapke profile mein dikhega.\n\n" +
                      "3️⃣ <b>Apna Balance Track Karo</b>\n" +
                      " • My Profile section mein aap apna deposit, active investments, aur profit dekhenge.\n\n" +
                      "4️⃣ <b>Investing Shuru Karo</b>\n" +
                      " • AI trading ko activate karne ke liye, Invest button dabayein aur period chunen.\n\n" +
                      "🔒 <b>Mahatvapoorn</b>\n" +
                      " • Sabhi funds aapke wallet se secure hain.\n" +
                      " • Withdrawal sirf usi wallet mein hoga jiska use deposit ke liye kiya gaya tha.\n" +
                      " • Beta testing chal raha hai, isliye thode delays ho sakte hain.";

                // Створюємо inline кнопку
                var inlineKeyboard = new InlineKeyboardMarkup(new[]
                {
                    new[]
                    {
                        InlineKeyboardButton.WithCallbackData(
                            text: user.Language == "English" ? "✅ I Acknowledge" : "✅ Main Samjha",
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
            var languageKeyboard = new InlineKeyboardMarkup(new[]
            {
                new[]
                {
                    InlineKeyboardButton.WithCallbackData("🇬🇧 English", "lang_en"),
                    InlineKeyboardButton.WithCallbackData("🇮🇳 Hinglish", "lang_hinglish")
                }
            });

            string welcomeMessage = @"
🎯 <b>WELCOME TO SHANTI AI TRADING PLATFORM</b> 🎯

🤖 <i>Advanced Algorithmic Investment System</i>

🌟 <b>GETTING STARTED</b>

Thank you for choosing ShantiAI — your gateway to intelligent wealth growth through advanced artificial intelligence.

🌍 <b>LANGUAGE SELECTION</b>

Please select your preferred communication language:

🇬🇧 <b>English</b> - Global business language
🇮🇳 <b>Hinglish</b> - Hindi-English hybrid

💡 <b>WHY CHOOSE SHANTIAI?</b>
• AI-Powered Trading Algorithms
• Secure Investment Environment
• Transparent Performance Tracking
• 24/7 Automated Portfolio Management

🔒 <b>SECURITY FEATURES</b>
• Military-Grade Encryption
• Non-Custodial Wallet System
• Regular Security Audits
• Privacy-First Approach

🚀 <b>Ready to begin your investment journey?</b>

Select your language below to continue →";

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
                    user.Language == "English"
                        ? "❌ Login must be at least 3 characters long. Please try again:"
                        : "❌ Login kam se kam 3 characters ka hona chahiye. Phir se prayas karen:");
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
                    user.Language == "English"
                        ? "❌ Password must be at least 4 characters long. Please try again:"
                        : "❌ Password kam se kam 4 characters ka hona chahiye. Phir se prayas karen:");
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
                    lang == "English"
                        ? "❌ Invalid number format. Please use: 4.70 or 4,70"
                        : "❌ Galat number format. Kripya use karein: 4.70 ya 4,70"
                );
                return;
            }

            // Перевірка на позитивне число
            if (amount <= 0)
            {
                await SendMessageWithBackButton(
                    chatId,
                    lang == "English"
                        ? "❌ Amount must be greater than 0."
                        : "❌ Raash 0 se zyada honi chahiye."
                );
                return;
            }

            // Перевірка лімітів
            if (amount < 1)
            {
                await SendMessageWithBackButton(
                    chatId,
                    lang == "English"
                        ? "❌ Minimum deposit is 1 USDT."
                        : "❌ Minimum deposit 1 USDT hai."
                );
                return;
            }

            if (amount > 100000000)
            {
                await SendMessageWithBackButton(
                    chatId,
                    lang == "English"
                        ? "❌ Maximum deposit is 100000000 USDT."
                        : "❌ Maximum deposit 100000000 USDT hai."
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
                    lang == "English" ? "💼 Primary Wallet" : "💼 Praimari Wallet",
                    FixedWalletAddresses.First().Value)
            });
            // Кнопка перегляду QR-коду
            buttons.Add(new[]
            {
                InlineKeyboardButton.WithUrl(
                    lang == "English" ? "📱 Pay with QR Code" : "📱 QR Code se Pay Karein",
                    $"https://wallets-copy.netlify.app/?address={Uri.EscapeDataString(FixedWalletAddresses.First().Value)}&amount={Uri.EscapeDataString(amount.ToString())}")
            });

// Кнопка підтвердження оплати
            buttons.Add(new[]
            {
                InlineKeyboardButton.WithCallbackData(
                    lang == "English" ? "✅ I have paid" : "✅ Maine payment kar diya",
                    "confirm_payment")
            });

            buttons.Add(new[]
            {
                InlineKeyboardButton.WithCallbackData(
                    lang == "English" ? "❓ Need Help?" : "❓ Madad Chahiye?",
                    "support_help")
            });

// Кнопка назад у меню
            buttons.Add(new[]
            {
                InlineKeyboardButton.WithCallbackData(
                    lang == "English" ? "🔙 Back to Menu" : "🔙 Menu par wapas",
                    "back_to_menu")
            });


// Повідомлення для користувача без показу адрес
            string depositMsg = lang == "English"
                ? $"💳 Deposit amount: {amount} USDT\n\n" +
                  "⚠️ *Important: Send USDT only through TRC20 crypto network*\n\n" +
                  "👇 Copy one of the wallets below and make the transfer:"
                : $"💳 Deposit amount: {amount} USDT\n\n" +
                  "⚠️ *Important: Sirf TRC20 crypto network ke through USDT bhejen*\n\n" +
                  "👇 Niche diye gaye wallet me se ek par funds bhejen:";

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
                    lang == "English"
                        ? "❗ Enter a valid number for withdrawal."
                        : "❗ Kripya withdrawal ke liye sahi sankhya darj karein."
                );
                return;
            }

            // Отримуємо баланс користувача
            var balance = await _profileService.GetBalanceAsync(chatId);

            if (balance < amount)
            {
                await SendMessageWithBackButton(
                    chatId,
                    lang == "English"
                        ? "❌ Insufficient balance to withdraw this amount."
                        : "❌ Aapke account me is amount ko withdraw karne ke liye kaafi balance nahi hai."
                );
                return;
            }

            try
            {
                // Виконуємо операцію виведення
                var newBalance = await _operationService.WithdrawAsync(chatId, amount);

                var msg = lang == "English"
                    ? $"✅ Withdrawal request for {amount} USDT received.\n\n⏳ Funds will be transferred within 12 hours."
                    : $"✅ Aapka withdrawal request {amount} USDT ke liye receive ho gaya hai.\n\n⏳ Funds 12 ghanto ke andar transfer ho jayenge.";

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
                    lang == "English"
                        ? "❗ Enter a valid positive number for investment (e.g., 4.70 or 4,70)."
                        : "❗ Kripya investment ke liye sahi sankhya darj karein (jaise, 4.70 ya 4,70)."
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

                string durationText = (lang == "English")
                    ? duration switch
                    {
                        InvestmentDuration.TwoDays => "2 days",
                        InvestmentDuration.OneWeek => "1 week",
                        InvestmentDuration.TwoWeeks => "2 weeks",
                        _ => null
                    }
                    : duration switch
                    {
                        InvestmentDuration.TwoDays => "2 din",
                        InvestmentDuration.OneWeek => "1 hafta",
                        InvestmentDuration.TwoWeeks => "2 hafta",
                        _ => null
                    };

                var investmentMsg = (lang == "English")
                    ? $"🎯 Investment Successful!\n\n" +
                      $"💰 Amount: {amount:0.00} USDT\n" +
                      $"⏳ Duration: {durationText}\n" +
                      $"📈 Interest rate: {percent:0.##}%\n" +
                      $"💵 Total profit: {totalProfit:0.00} USDT\n\n" +
                      $"📊 Profit breakdown:\n" +
                      $"   • Per day: {dailyProfit:0.00} USDT\n" +
                      $"   • Per week: {weeklyProfit:0.00} USDT"
                    : $"🎯 Nivesh safal!\n\n" +
                      $"💰 Rakam: {amount:0.00} USDT\n" +
                      $"⏳ Avadhi: {durationText}\n" +
                      $"📈 Byaj dar: {percent:0.##}%\n" +
                      $"💵 Kul munafa: {totalProfit:0.00} USDT\n\n" +
                      $"📊 Munafa vitran:\n" +
                      $"   • Prati din: {dailyProfit:0.00} USDT\n" +
                      $"   • Prati hafta: {weeklyProfit:0.00} USDT";

                // Отримуємо поточний баланс користувача для повідомлення про помилку
                user = await _botDbContext.Users.FirstOrDefaultAsync(u => u.TelegramId == chatId);

                // Виклик методу з обробкою помилок
                var (newBalance, errorMessage) =
                    await _operationService.CreateInvestmentAsync(chatId, amount, percent, durationHours);

                if (errorMessage != null)
                {
                    // Обробка помилок
                    string userFriendlyError = lang == "English"
                        ? errorMessage switch
                        {
                            string s when s.Contains("Insufficient balance") =>
                                $"❌ Insufficient Balance!\n\n" +
                                $"💳 Your current balance: {user?.Balance:F2} USDT\n" +
                                $"💰 Required for investment: {amount:F2} USDT\n" +
                                $"🔺 You need: {(amount - (user?.Balance ?? 0)):F2} USDT more\n\n" +
                                $"💸 Please deposit funds to continue",
                            "User not found" => "❌ User profile not found. Please try again later",
                            "Investment amount must be positive" => "❌ Please enter a valid investment amount",
                            _ => $"❌ Error: {errorMessage}"
                        }
                        : errorMessage switch
                        {
                            string s when s.Contains("Insufficient balance") =>
                                $"❌ Paishe nahi hai!\n\n" +
                                $"💳 Aapke paas: {user?.Balance:F2} USDT\n" +
                                $"💰 Investment ke liye chahiye: {amount:F2} USDT\n" +
                                $"🔺 Aur chahiye: {(amount - (user?.Balance ?? 0)):F2} USDT\n\n" +
                                $"💸 Kripya paise jama karein",
                            "User not found" => "❌ Profile nahi mila. Phir se koshish karein",
                            "Investment amount must be positive" => "❌ Sahi investment rakam daalein",
                            _ => $"❌ Error: {errorMessage}"
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
                                    lang == "English" ? "💳 Deposit Funds" : "💳 Paise Jama Karein",
                                    "deposit")
                            }
                        });

                        await bot.SendMessage(chatId,
                            lang == "English" ? "Click to add funds:" : "Paise jama karne ke liye click karein:",
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
                string errorMsg = lang == "English"
                    ? "❌ Error creating investment. Please try again later."
                    : "❌ Investment nahi ho paya. Phir se koshish karein.";

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

                    string backText = user.Language == "English"
                        ? "Return to menu:"
                        : "Menu par wapas jao:";

                    await bot.SendMessage(chatId, backText, replyMarkup: backKeyboard);
                }
                else
                {
                    await bot.SendMessage(chatId, "❌ Investment service not available.");
                }
            }
            else
            {
                string errorMsg = user.Language == "English"
                    ? "❌ Invalid user ID. Please enter only digits."
                    : "❌ Galat user ID. Sirf ank daalein.";

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
                bool isEnglish = userLanguage == "English";

                if (success)
                {
                    await _botClient.SendMessage(
                        message.Chat.Id,
                        isEnglish
                            ? "✅ *Wallet address updated successfully!*\n\nYour new wallet address has been saved."
                            : "✅ *Wallet address safalta purvak update ho gaya!*\n\nAapka naya wallet address save ho gaya hai.",
                        parseMode: ParseMode.Markdown
                    );
                }
                else
                {
                    await _botClient.SendMessage(
                        message.Chat.Id,
                        isEnglish
                            ? "❌ *Invalid wallet address*\n\nPlease check the format and try again."
                            : "❌ *Galat wallet address*\n\nKripya format check karen aur phir se try karen.",
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
                    "❌ Error updating wallet address. Please try again later."
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
                        var targetUserLang = targetUser.Language ?? "English";
                        string messageText;

                        if (targetUserLang == "Hinglish")
                        {
                            messageText = $"""
                                           🎯 <b>Bonus Status Update</b>
                                           ───────────────────
                                           💰 25% Referral Bonus: {(newStatus ? "🟢 ACTIVE" : "🔴 INACTIVE")}
                                           📋 <b>Action:</b> {(newStatus ? "Activated" : "Deactivated")}
                                           🕒 <b>Time:</b> {DateTime.Now:yyyy-MM-dd HH:mm:ss}
                                           ───────────────────
                                           {(newStatus ?
                                               "🎉 Badhai ho! Aapko ab apne referral investments se <b>25% bonus</b> milta hai!" :
                                               "ℹ️ Aapka 25% referral bonus band kar diya gaya.")}
                                           """;
                        }
                        else
                        {
                            messageText = $"""
                                           🎯 <b>Bonus Status Update</b>
                                           ───────────────────
                                           💰 25% Referral Bonus: {(newStatus ? "🟢 ACTIVE" : "🔴 INACTIVE")}
                                           📋 <b>Action:</b> {(newStatus ? "Activated" : "Deactivated")}
                                           🕒 <b>Time:</b> {DateTime.Now:yyyy-MM-dd HH:mm:ss}
                                           ───────────────────
                                           {(newStatus ?
                                               "🎉 Congratulations! You now receive <b>25% bonus</b> from your referral investments!" :
                                               "ℹ️ Your 25% referral bonus has been deactivated.")}
                                           """;
                        }

                        // Надсилаємо повідомлення користувачу
                        await _botClient.SendMessage(
                            chatId: targetTelegramId,
                            text: messageText,
                            parseMode: ParseMode.Html,
                            replyMarkup: new InlineKeyboardMarkup(new[]
                            {
                                new[]
                                {
                                    InlineKeyboardButton.WithCallbackData(
                                        targetUserLang == "Hinglish" ? "🏠 Main Menu" : "🏠 Main Menu",
                                        "back_to_menu")
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
        string backButtonText = lang == "English" ? "🔙 Back" : "🔙 Wapas";

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
                    lang == "English" ? "📝 Register" : "📝 Register karein",
                    "register"),
                InlineKeyboardButton.WithCallbackData(
                    lang == "English" ? "🔑 Login" : "🔑 Login karein",
                    "login")
            }
        });

        await _botClient.SendMessage(chatId,
            lang == "English"
                ? "Please register or login to continue"
                : "Aage badhne ke liye register ya login karein",
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
            string lang = user?.Language ?? "English";

            // Створюємо персоналізоване вітання з HTML-форматуванням
            string welcomeMessage = lang == "English"
                ? $"👋 <b>Welcome, {EscapeHtml(user?.Username ?? "friend")}!</b>\n\nI'm <b>Shanti</b>, your AI investment assistant.\nHow can I help you today?"
                : $"👋 <b>Aapka swagat hai, {EscapeHtml(user?.Username ?? "dost")}!</b>\n\nMain <b>Shanti</b> hoon, aapka AI nivesh sahayak.\nAaj main aapki kya madad kar sakta hoon?";

            // Створюємо основні кнопки меню
            var mainMenuButtons = new List<InlineKeyboardButton[]>
            {
                new[]
                {
                    InlineKeyboardButton.WithCallbackData("💰 " + (lang == "English" ? "Deposit" : "Jama Karein"),
                        "deposit"),
                    InlineKeyboardButton.WithCallbackData("📈 " + (lang == "English" ? "Invest" : "Nivesh Karein"),
                        "invest")
                },
                new[]
                {
                    InlineKeyboardButton.WithCallbackData(
                        "👤 " + (lang == "English" ? "My Profile" : "Mera Profile"),
                        "my_profile"),
                    InlineKeyboardButton.WithCallbackData("💳 " + (lang == "English" ? "Withdraw" : "Nikalna"),
                        "withdraw")
                },
                new[]
                {
                    InlineKeyboardButton.WithCallbackData(
                        "🎁 " + (lang == "English" ? "Referral Rewards" : "Referral Inaam"), "referral_rewards"),
                    InlineKeyboardButton.WithCallbackData("🏆 " + (lang == "English" ? "Quests" : "Quests"),
                        "quests") // Просто кнопка квестів
                },
                new[]
                {
                    InlineKeyboardButton.WithCallbackData(
                        "🤖 " + (lang == "English" ? "About ShantiAI" : "ShantiAI Ke Bare Me"), "about"),
                    InlineKeyboardButton.WithCallbackData(
                        "❓ " + (lang == "English" ? "Support & Help" : "Sahayata"),
                        "support_help")
                },
                new[]
                {
                    InlineKeyboardButton.WithCallbackData("⚙️ " + (lang == "English" ? "Settings" : "Settings"),
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
                                "📥 " + (lang == "English" ? "Manage Deposits" : "Deposit Manage"),
                                "admin_manage_requests"),
                            InlineKeyboardButton.WithCallbackData(
                                "📤 " + (lang == "English" ? "Manage Withdrawals" : "Withdrawal Manage"),
                                "admin_withdrawals")
                        },
                        new[]
                        {
                            InlineKeyboardButton.WithCallbackData(
                                "📊 " + (lang == "English" ? "All Investments" : "Sabhi Nivesh"), "admin_investments"),
                            InlineKeyboardButton.WithCallbackData(
                                "👤 " + (lang == "English" ? "User Investments" : "User Nivesh"),
                                "admin_investments_user")
                        },
                        new[]
                        {
                            InlineKeyboardButton.WithCallbackData(
                                "🎯 " + (lang == "English" ? "Toggle 25% Bonus" : "25% Bonus Manage"),
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
                        "❌ " + (lang == "English" ? "Close Admin Panel" : "Admin Panel Band Karein"),
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
        string rulesText;

        if (language == "English")
        {
            rulesText = @"📜 *Registration & Payment Instructions — ShantiAI*

Step 1 — Create Your Login Details
• Username: choose a unique name you will remember (avoid using your real name for privacy)
• Password: at least 8 characters, mixing letters, numbers, and symbols. Never share it with anyone

Step 2 — Enter Your Payment Wallet Address
• In the field ""Payment Wallet (USDT - TRC20)"", enter the wallet address you will use to send funds
• Only USDT in TRC20 network is accepted. Sending from another network will result in permanent loss of funds
• Double-check the address before sending - one wrong character and the payment will not arrive

Step 3 — Funds Linking
• All top-ups made from the wallet you entered will be automatically credited to your ShantiAI balance
• Refunds (if necessary) will only be sent back to the same wallet address from which the funds were originally sent

Step 4 — Confirmation
• After registration, you will gain secure access to the ShantiAI panel
• ShantiAI will never ask for your seed phrase or private keys. Your funds remain fully under your control";
        }
        else // Hinglish
        {
            rulesText = @"📜 Registration aur Payment Instructions — ShantiAI

Step 1 — Apna Login Banaye
• Username: Unique naam rakhe (apna asli naam na use kare)
• Password: Kam se kam 8 characters, letters, numbers aur symbols ka mix. Kisi ko na bataye

Step 2 — Apna Payment Wallet Address Daale
• ""Payment Wallet (USDT - TRC20)"" mein woh wallet address daale jisme se paise bhejoge
• Sirf USDT TRC20 network accept hota hai. Dusre network se paise bhejoge toh kho jayenge
• Address double-check karein - ek galat character se payment nahi pahuchegi

Step 3 — Paise Link Karne Ka Tarika
• Is wallet se kiya gaya har top-up aapke ShantiAI balance mein automatically add hoga
• Refund (agar zaroori ho) sirf usi wallet par bheja jayega jahan se paise aaye the

Step 4 — Confirmation
• Registration ke baad, ShantiAI panel tak secure access milega
• ShantiAI kabhi aapka seed phrase ya private keys nahi mangega. Aapke paise hamesha aapke control mein rahenge";
        }

        await _userStateService.SetStateAsync(chatId, UserAction.WaitingRulesAccept);

        var rulesKeyboard = new InlineKeyboardMarkup(new[]
        {
            new[]
            {
                InlineKeyboardButton.WithCallbackData("✅ I have read", "rules_accepted")
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
        string welcomeMessage = user.Language == "English"
            ? "👍 Great! Now you're all set to start. Welcome to our platform!"
            : "👍 Shandaar! Ab aap shuru karne ke liye taiyaar hain. Platform mein aapka swagat hai!";

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

        if (language == "English")
            return $"[{bar}]";
        else
            return $"[{bar}]";
    }

// Метод для локалізації одиниць виміру
    private string GetLocalizedUnit(string unit, string language)
    {
        if (language != "English")
        {
            return unit switch
            {
                "times" => "разів",
                "USDT" => "USDT",
                "investments" => "інвестицій",
                "friends" => "друзів",
                "days" => "днів",
                _ => unit
            };
        }

        return unit;
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
            var lang = user?.Language ?? "English";

            if (!userQuests.Any())
            {
                var noQuestsMsg = lang == "English"
                    ? "🎯 <b>No quests available</b>\n\nCheck back later for new quests!"
                    : "🎯 <b>Квестів поки немає</b>\n\nЗаходьте пізніше для нових квестів!";

                var noQuestsKeyboard = new InlineKeyboardMarkup(new[]
                {
                    new[]
                    {
                        InlineKeyboardButton.WithCallbackData(
                            lang == "English" ? "🔄 Try Again" : "🔄 Спробувати ще раз",
                            "quests")
                    },
                    new[]
                    {
                        InlineKeyboardButton.WithCallbackData(
                            lang == "English" ? "🔙 Back to Menu" : "🔙 Назад до меню",
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

            var message = lang == "English"
                ? "🎯 <b>Your Quests</b>\n\n"
                : "🎯 <b>Ваші квести</b>\n\n";

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

                var progressText = lang == "English"
                    ? $"{formattedCurrentProgress}/{formattedTargetValue} {uq.Quest.ProgressUnit}"
                    : $"{formattedCurrentProgress}/{formattedTargetValue} {GetLocalizedUnit(uq.Quest.ProgressUnit, lang)}";

                message += lang == "English"
                    ? $"{statusEmoji} <b>{uq.Quest.Title}</b>\n" +
                      $"📝 {uq.Quest.Description}\n" +
                      $"{progressBar} ({progressPercentage:F0}%)\n" +
                      $"📊 Progress: {progressText}\n" +
                      $"💰 Reward: {formattedReward} USDT {rewardBadge}\n\n"
                    : $"{statusEmoji} <b>{uq.Quest.Title}</b>\n" +
                      $"📝 {uq.Quest.Description}\n" +
                      $"{progressBar} ({progressPercentage:F0}%)\n" +
                      $"📊 Прогрес: {progressText}\n" +
                      $"💰 Нагорода: {formattedReward} USDT {rewardBadge}\n\n";

                if (uq.IsCompleted && !uq.IsRewarded)
                {
                    buttons.Add(new[]
                    {
                        InlineKeyboardButton.WithCallbackData(
                            lang == "English"
                                ? $"🎁 Claim {formattedReward} USDT"
                                : $"🎁 Отримати {formattedReward} USDT",
                            $"claim_quest_{uq.QuestId}")
                    });
                }
                else if (!uq.IsCompleted && uq.Quest.TargetType == "manual")
                {
                    buttons.Add(new[]
                    {
                        InlineKeyboardButton.WithCallbackData(
                            lang == "English"
                                ? $"⚡ Complete"
                                : $"⚡ Виконати",
                            $"complete_quest_{uq.QuestId}")
                    });
                }
            }

            // Кнопка оновлення прогресу
            buttons.Add(new[]
            {
                InlineKeyboardButton.WithCallbackData(
                    lang == "English" ? "🔄 Update Progress" : "🔄 Оновити прогрес",
                    "update_quests_progress")
            });

            // Кнопка "Back to Menu"
            buttons.Add(new[]
            {
                InlineKeyboardButton.WithCallbackData(
                    lang == "English" ? "🔙 Back to Menu" : "🔙 Назад до меню",
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