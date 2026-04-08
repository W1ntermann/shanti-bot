using Microsoft.EntityFrameworkCore;
using ShantiBotDi.Db;
using Telegram.Bot;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace ShantiBotDi.Services;

public class MaintenanceService : IMaintenanceService
{
    private readonly ITelegramBotClient _botClient;
    private readonly BotDbContext _context;
    private readonly ILocalizationService _localizationService;

    public MaintenanceService(
        ITelegramBotClient botClient,
        BotDbContext context,
        ILocalizationService localizationService)
    {
        _botClient = botClient;
        _context = context;
        _localizationService = localizationService;
    }

    public async Task NotifyMaintenanceStartAsync(string reason)
    {
        // Отримуємо тільки зареєстрованих користувачів
        var users = await _context.Users
            .AsNoTracking()
            .Where(u => u.IsAuthorized)
            .Select(u => new { u.TelegramId, u.Language })
            .ToListAsync();
        
        foreach (var user in users)
        {
            try
            {
                string message = GetMaintenanceMessage(user.Language, reason);
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
                Console.WriteLine($"Error sending maintenance start to {user.TelegramId}: {ex.Message}");
            }
        }
    }

    public async Task NotifyMaintenanceEndAsync()
    {
        // Отримуємо тільки зареєстрованих користувачів
        var users = await _context.Users
            .AsNoTracking()
            .Where(u => u.IsAuthorized)
            .Select(u => new { u.TelegramId, u.Language })
            .ToListAsync();
        
        foreach (var user in users)
        {
            try
            {
                string message = GetRestoredMessage(user.Language);
                await _botClient.SendMessage(
                    user.TelegramId, 
                    message, 
                    parseMode: ParseMode.Html
                );
                
                await Task.Delay(100);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error sending maintenance end to {user.TelegramId}: {ex.Message}");
            }
        }
    }

    private string GetMaintenanceMessage(string lang, string reason)
    {
        return _localizationService.GetText(lang, "maintenance.start", reason);
    }

    private string GetRestoredMessage(string lang)
    {
        return _localizationService.GetText(lang, "maintenance.end");
    }
}