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

    public WithdrawalAdminService(
        IManageService manageService,
        IOperationService operationService,
        ITelegramBotClient bot)
    {
        _manageService = manageService;
        _operationService = operationService;
        _bot = bot;
    }

    
    public async Task ShowPendingWithdrawalsAsync(long chatId)
    {
        var adminUser = await _manageService.GetUserByTelegramIdAsync(chatId);
        if (adminUser == null || adminUser.Role != UserRole.Admin)
        {
            await _bot.SendMessage(chatId, "❌ Access denied.");
            return;
        }

        var pendingRequests = await _operationService.GetPendingWithdrawalRequestsAsync();

        if (pendingRequests.Count == 0)
        {
            await _bot.SendMessage(chatId, "✅ No pending withdrawal requests.");
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
        if (adminUser == null || adminUser.Role != UserRole.Admin)
        {
            await _bot.SendMessage(chatId, "❌ Access denied.");
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
        if (adminUser == null || adminUser.Role != UserRole.Admin)
        {
            await _bot.SendMessage(chatId, "❌ Access denied.");
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
                var userLang = request.User.Language ?? "English";
                var userMessage = userLang == "English"
                    ? $"✅ Your withdrawal request for {request.Amount} USDT has been successfully approved.\n\n" +
                      "We sincerely appreciate your trust in our service and look forward to serving you again."
                    : $"✅ Aapka {request.Amount} USDT withdrawal request safal tareeke se approve ho gaya.\n\n" +
                      "Hamare service par aapke vishwas ke liye dhanyavaad. Aapka swagat hai humari seva mein phir se.";

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