using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using ShantiBotDi.Db;
using ShantiBotDi.Models;

namespace ShantiBotDi.Services;

public class AuthService : IAuthService
{
    private readonly BotDbContext _db;

    public AuthService(BotDbContext db)
    {
        _db = db;
    }

    public async Task<bool> RegisterAsync(long telegramId, string username, string password)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.TelegramId == telegramId);
        if (user != null)
            return false;

        var passwordHash = HashPassword(password);

        _db.Users.Add(new User
        {
            TelegramId = telegramId,
            Username = username,
            PasswordHash = passwordHash,
            IsAuthorized = false
        });

        await _db.SaveChangesAsync();
        return true;
    }

    public async Task<string> LoginAsync(long telegramId, string password)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.TelegramId == telegramId);
        if (user == null)
            return null;

        if (user.PasswordHash != HashPassword(password))
            return null;

        // Генеруємо токен сесії
        user.SessionToken = Guid.NewGuid().ToString();
        user.IsAuthorized = true;

        await _db.SaveChangesAsync();
        return user.SessionToken;
    }

    public async Task<bool> IsAuthorizedAsync(long telegramId, string token)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.TelegramId == telegramId);
        return user != null && user.IsAuthorized && user.SessionToken == token;
    }

    public async Task LogoutAsync(long telegramId)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.TelegramId == telegramId);
        if (user != null)
        {
            user.IsAuthorized = false;
            user.SessionToken = null;
            await _db.SaveChangesAsync();
        }
    }

    public string HashPassword(string password)
    {
        using var sha256 = SHA256.Create();
        var bytes = Encoding.UTF8.GetBytes(password);
        var hash = sha256.ComputeHash(bytes);
        return Convert.ToBase64String(hash);
    }

    public async Task<bool> ValidateLoginAsync(string username, string password)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Username == username);
        if (user == null)
            return false;

        var passwordHash = HashPassword(password);
        return user.PasswordHash == passwordHash;
    }
}