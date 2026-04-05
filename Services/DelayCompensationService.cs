using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ShantiBotDi.Db;
using Telegram.Bot;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace ShantiBotDi.Services;

public class DelayCompensationService : IDelayCompensationService
{
    private readonly ITelegramBotClient _botClient;
    private readonly BotDbContext _context;
    private readonly ILogger<DelayCompensationService> _logger;

    public DelayCompensationService(
        ITelegramBotClient botClient,
        BotDbContext context,
        ILogger<DelayCompensationService> logger)
    {
        _botClient = botClient;
        _context = context;
        _logger = logger;
    }

    public async Task NotifyDelayAndCompensateAsync()
    {
        // Отримуємо всіх авторизованих користувачів
        var users = await _context.Users
            .Where(u => u.IsAuthorized)
            .ToListAsync();

        int successCount = 0;
        var failedUsers = new List<long>();

        foreach (var user in users)
        {
            try
            {
                user.Balance += 3m;

                string message = GetDelayMessage(user.Language);
                await _botClient.SendMessage(
                    user.TelegramId,
                    message,
                    parseMode: ParseMode.Html,
                    replyMarkup: new ReplyKeyboardRemove()
                );

                successCount++;

                
                await Task.Delay(100);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing delay notification for user {TelegramId}", user.TelegramId);
                failedUsers.Add(user.TelegramId);
            }
        }

        
        await _context.SaveChangesAsync();

        
        _logger.LogInformation(
            "Delay compensation completed. Success: {SuccessCount}, Failed: {FailedCount}",
            successCount,
            failedUsers.Count);
    }

    public async Task NotifyDelayAndCompensateWithCustomReasonAsync(string customReason)
    {
        var users = await _context.Users
            .Where(u => u.IsAuthorized)
            .ToListAsync();

        foreach (var user in users)
        {
            try
            {
                // Додаємо 3 USDT до балансу
                user.Balance += 3m;

                string message = GetDelayMessage(user.Language, customReason);
                await _botClient.SendMessage(
                    user.TelegramId,
                    message,
                    parseMode: ParseMode.Html,
                    replyMarkup: new ReplyKeyboardRemove()
                );

                await Task.Delay(100);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing delay notification for user {TelegramId}", user.TelegramId);
            }
        }

        await _context.SaveChangesAsync();
    }

    private string GetDelayMessage(string lang, string? customReason = null)
    {
        string reason = customReason ?? "market conditions ke karan";

        return lang == "Hinglish"
            ? GetHinglishMessage(reason)
            : GetEnglishMessage(reason);
    }

    private string GetHinglishMessage(string reason)
    {
        return $@"<b>📢 Important Update</b>

Dear Trader,

Market {reason} exchange se payouts me 5 days ki delay ho rahi hai. Yeh situation temporary hai aur hum issue resolve karne me lage huye hain.

<b>✅ Aapke patience ke liye:</b>
Hum aapke account me <b>3 USDT bonus</b> add kar diye hain! Yeh amount aap trading ya withdrawal ke liye use kar sakte hain.

🙏 Aapke support ke liye dhanyavaad!

<b>🔜 Updates:</b>
Jaisi hi situation normal hogi, hum aapko notify kar denge.

—
Shanti Team ❤️";
    }

    private string GetEnglishMessage(string reason)
    {
        return $@"<b>📢 Important Update</b>

Dear Trader,

Due to {reason}, there is a 5-day delay in payouts from the exchange we work with. This is a temporary situation and we are working to resolve the issue.

<b>✅ For your patience:</b>
We have added <b>3 USDT bonus</b> to your account! You can use this amount for trading or withdrawal.

🙏 Thank you for your understanding and support!

<b>🔜 Updates:</b>
We will notify you as soon as the situation returns to normal.

—
Shanti Team ❤️";
    }

    public async Task<int> GetAffectedUsersCountAsync()
    {
        return await _context.Users
            .Where(u => u.IsAuthorized)
            .CountAsync();
    }
}