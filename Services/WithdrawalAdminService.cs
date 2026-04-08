using ShantiBotDi.Models;
using Telegram.Bot;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace ShantiBotDi.Services;

public class WithdrawalAdminService
{
    private readonly IManageService _manageService;
    private readonly IOperationService _operationService;
    private readonly ITelegramBotClient _bot;
    private readonly ILocalizationService _localizationService;

    public WithdrawalAdminService(
        IManageService manageService,
        IOperationService operationService,
        ITelegramBotClient bot,
        ILocalizationService localizationService)
    {
        _manageService = manageService;
        _operationService = operationService;
        _bot = bot;
        _localizationService = localizationService;
    }

    private string T(string? language, string english, string hinglish, string russian, string farsi, string arabic, string chinese)
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

    
    public async Task ShowPendingWithdrawalsAsync(long chatId)
    {
        var adminUser = await _manageService.GetUserByTelegramIdAsync(chatId);
        var lang = adminUser?.PreferredLanguage ?? adminUser?.Language ?? BotLanguageCodes.English;
        if (adminUser == null || adminUser.Role != UserRole.Admin)
        {
            await _bot.SendMessage(chatId, T(lang, "❌ Access denied.", "❌ Access nahi hai.", "❌ Доступ запрещен.", "❌ دسترسی مجاز نیست.", "❌ تم رفض الوصول.", "❌ 无权访问。"));
            return;
        }

        var pendingRequests = await _operationService.GetPendingWithdrawalRequestsAsync();

        if (pendingRequests.Count == 0)
        {
            await _bot.SendMessage(chatId, T(lang, "✅ No pending withdrawal requests.", "✅ Koi pending withdrawal request nahi hai.", "✅ Нет ожидающих заявок на вывод.", "✅ هیچ درخواست برداشت در انتظاری وجود ندارد.", "✅ لا توجد طلبات سحب معلقة.", "✅ 当前没有待处理的提现请求。"));
            return;
        }

        foreach (var request in pendingRequests)
        {
            var userName = request.User?.Username ?? "Unknown";
            var walletAddress = request.User?.WalletAddress ?? "Not specified";

            var messageText =
                $"📥 <b>Withdrawal Request #{request.Id}</b>\n\n" +
                $"👤 <b>User:</b> @{userName} (ID: {request.UserId})\n" +
                $"💳 <b>Wallet:</b> <code>{walletAddress}</code>\n" +
                $"💰 <b>Amount:</b> {request.Amount:N2} USDT\n" +
                $"🎯 <b>Type:</b> {(request.IsFullWithdrawal ? "Full Balance" : "Partial")}\n" +
                $"📅 <b>Created:</b> {request.CreatedAt:dd.MM.yyyy HH:mm}\n" +
                $"📊 <b>Status:</b> {request.Status}";

            var buttons = new InlineKeyboardMarkup(new[]
            {
                new[]
                {
                    InlineKeyboardButton.WithCallbackData("✅ Approve", $"approve_withdraw_{request.Id}"),
                    InlineKeyboardButton.WithCallbackData("❌ Reject", $"reject_withdraw_{request.Id}")
                },
                new[]
                {
                    InlineKeyboardButton.WithCallbackData("🔙 Back to Menu", "back_to_menu")
                }
            });

            await _bot.SendMessage(chatId, messageText, parseMode: ParseMode.Html, replyMarkup: buttons);
        }
    }

    public async Task RejectWithdrawalAsync(long chatId, int requestId)
    {
        var adminUser = await _manageService.GetUserByTelegramIdAsync(chatId);
        var lang = adminUser?.PreferredLanguage ?? adminUser?.Language ?? BotLanguageCodes.English;
        if (adminUser == null || adminUser.Role != UserRole.Admin)
        {
            await _bot.SendMessage(chatId, T(lang, "❌ Access denied.", "❌ Access nahi hai.", "❌ Доступ запрещен.", "❌ دسترسی مجاز نیست.", "❌ تم رفض الوصول.", "❌ 无权访问。"));
            return;
        }

        var request = await _operationService.GetWithdrawalRequestByIdAsync(requestId);
        if (request == null)
        {
            await _bot.SendMessage(chatId, $"❌ Withdrawal request #{requestId} not found.");
            return;
        }

        var success = await _operationService.RejectWithdrawalRequestAsync(requestId);
        if (success)
        {
            await _bot.SendMessage(chatId, $"✅ Withdrawal request #{requestId} rejected.");

            if (request.User?.TelegramId != null)
            {
                await _bot.SendMessage(request.User.TelegramId,
                    $"❌ Your withdrawal request for {request.Amount} USDT has been rejected.");
            }

            // Оновлюємо список
            await ShowPendingWithdrawalsAsync(chatId);
        }
        else
        {
            await _bot.SendMessage(chatId, $"❌ Could not reject withdrawal request #{requestId}.");
        }
    }

    public async Task<long?> ApproveWithdrawalAsync(long chatId, int requestId)
    {
        var adminUser = await _manageService.GetUserByTelegramIdAsync(chatId);
        var lang = adminUser?.PreferredLanguage ?? adminUser?.Language ?? BotLanguageCodes.English;
        if (adminUser == null || adminUser.Role != UserRole.Admin)
        {
            await _bot.SendMessage(chatId, T(lang, "❌ Access denied.", "❌ Access nahi hai.", "❌ Доступ запрещен.", "❌ دسترسی مجاز نیست.", "❌ تم رفض الوصول.", "❌ 无权访问。"));
            return null;
        }

        var request = await _operationService.GetWithdrawalRequestByIdAsync(requestId);
        if (request == null)
        {
            await _bot.SendMessage(chatId, $"❌ Withdrawal request #{requestId} not found.");
            return null;
        }

        var success = await _operationService.ApproveWithdrawalRequestAsync(requestId);
        if (success)
        {
            // Admin notification (always in English)
            await _bot.SendMessage(chatId, $"✅ Withdrawal request #{requestId} approved.");

            if (request.User?.TelegramId != null)
            {
                // User notification in their preferred language
                                var userLang = request.User.PreferredLanguage ?? request.User.Language ?? BotLanguageCodes.English;
                                var userMessage = T(
                                        userLang,
                                        $"✅ Your withdrawal request for {request.Amount} USDT has been successfully approved.\n\nWe sincerely appreciate your trust in our service and look forward to serving you again.",
                                        $"✅ Aapka {request.Amount} USDT withdrawal request safal tareeke se approve ho gaya.\n\nHamare service par aapke vishwas ke liye dhanyavaad. Aapka swagat hai humari seva mein phir se.",
                                        $"✅ Ваш запрос на вывод {request.Amount} USDT успешно одобрен.\n\nМы искренне ценим ваше доверие к нашему сервису и будем рады помочь вам снова.",
                                        $"✅ درخواست برداشت {request.Amount} USDT شما با موفقیت تایید شد.\n\nاز اعتماد شما به خدمات ما صمیمانه سپاسگزاریم و خوشحال می شویم دوباره در خدمت شما باشیم.",
                                        $"✅ تمت الموافقة بنجاح على طلب سحب {request.Amount} USDT الخاص بك.\n\nنحن نقدر ثقتك بخدمتنا ونتطلع لخدمتك مرة أخرى.",
                                        $"✅ 您提取 {request.Amount} USDT 的请求已成功批准。\n\n衷心感谢您对我们服务的信任，期待再次为您服务。"
                                );

                await _bot.SendMessage(request.User.TelegramId, userMessage);

                return request.User.TelegramId;
            }

            // Update admin's pending withdrawals list
            await ShowPendingWithdrawalsAsync(chatId);
        }
        else
        {
            await _bot.SendMessage(chatId, $"❌ Could not approve withdrawal request #{requestId}.");
        }

        return null;
    }
    
}