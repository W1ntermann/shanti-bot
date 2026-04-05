using Microsoft.EntityFrameworkCore;
using ShantiBotDi.Db;
using ShantiBotDi.Models;

namespace ShantiBotDi.Services;

public class UserStateService : IUserStateService
{
    private readonly BotDbContext _db;

    public UserStateService(BotDbContext db)
    {
        _db = db;
    }

    public async Task SetStateAsync(long userId, UserAction action)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.TelegramId == userId);
        if (user is null)
            throw new Exception("User not found");

        user.State = action;
        await _db.SaveChangesAsync();
    }

    public async Task<UserAction> GetStateAsync(long userId)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.TelegramId == userId);
        return user?.State ?? UserAction.None;
    }

    public async Task ClearStateAsync(long userId)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.TelegramId == userId);
        if (user is not null)
        {
            user.State = UserAction.None;
            await _db.SaveChangesAsync();
        }
    }
    public async Task<User> GetByChatIdAsync(long chatId)
    {
        return await _db.Users.FirstAsync(u => u.TelegramId == chatId);
    }

    public async Task<int> GetReferralCountAsync(long telegramId)
    {
        // Знаходимо користувача за telegramId
        var user = await _db.Users.FirstOrDefaultAsync(u => u.TelegramId == telegramId);
        if (user == null) return 0;
    
        // Рахуємо користувачів, яких запросив цей користувач
        return await _db.Users.CountAsync(u => u.ReferredBy == user.Id);
    }
    
    public async Task SetTempUsernameAsync(long userId, string tempUsername)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.TelegramId == userId);
        if (user is null) throw new Exception("User not found");
    
        user.TempUsername = tempUsername;
        await _db.SaveChangesAsync();
    }

    public async Task<string?> GetTempUsernameAsync(long userId)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.TelegramId == userId);
        return user?.TempUsername;
    }

    public async Task ClearTempUsernameAsync(long userId)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.TelegramId == userId);
        if (user != null)
        {
            user.TempUsername = null;
            await _db.SaveChangesAsync();
        }
    }
}