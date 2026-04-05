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

    public MaintenanceService(ITelegramBotClient botClient, BotDbContext context)
    {
        _botClient = botClient;
        _context = context;
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
        return lang == "Hinglish"
            ? $"🔧 <b>Technical Maintenance</b>\n\nBot temporarily unavailable hai.\n<b>Reason:</b> {reason}\n\nPlease wait karo!"
            : $"🔧 <b>Technical Maintenance</b>\n\nBot temporarily unavailable.\n<b>Reason:</b> {reason}\n\nPlease wait!";
    }

    private string GetRestoredMessage(string lang)
    {
        return lang == "Hinglish"
            ? "✅ <b>Bot Restored</b>\n\nBot ab available hai and working normally!\nThank you for waiting!"
            : "✅ <b>Bot Restored</b>\n\nBot is now available and working normally!\nThank you for waiting!";
    }
}