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
    private readonly ILocalizationService _localizationService;

    public DelayCompensationService(
        ITelegramBotClient botClient,
        BotDbContext context,
        ILogger<DelayCompensationService> logger,
        ILocalizationService localizationService)
    {
        _botClient = botClient;
        _context = context;
        _logger = logger;
        _localizationService = localizationService;
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
        string reason = customReason ?? _localizationService.GetText(lang, "delay.defaultReason");
        return _localizationService.GetText(lang, "delay.message", reason);
    }

    public async Task<int> GetAffectedUsersCountAsync()
    {
        return await _context.Users
            .Where(u => u.IsAuthorized)
            .CountAsync();
    }
}