using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using ShantiBotDi.Db;
using ShantiBotDi.Models;

namespace ShantiBotDi.Services;

public class ManageService : IManageService
{
    private readonly BotDbContext _context;
    private readonly IAuthService _authService;
    private readonly ILocalizationService _localizationService;

    public ManageService(BotDbContext context, IAuthService authService, ILocalizationService localizationService)
    {
        _context = context;
        _authService = authService;
        _localizationService = localizationService;
    }

    public async Task<List<User>> GetUsersAsync()
    {
        return await _context.Users.ToListAsync();
    }

    public Task<User> RegisterUserIfNotExistsAsync(long telegramId, string? username, long? referredBy = null)
    {
        throw new NotImplementedException();
    }

    public async Task<User?> FindOrCreateTempUserAsync(long telegramId, string? username, string? referralCode = null)
    {
        var user = await _context.Users
            .FirstOrDefaultAsync(u => u.TelegramId == telegramId);

        if (user != null)
        {
            // ✅ Якщо користувач вже існує, але має реферальний код - оновлюємо
            if (!string.IsNullOrEmpty(referralCode) && user.PendingReferralCode == null)
            {
                user.PendingReferralCode = referralCode;
                await _context.SaveChangesAsync();
            }
            return user;
        }

        // Створюємо ТИМЧАСОВУ заготовку користувача
        user = new User
        {
            TelegramId = telegramId,
            Username = $"temp_{telegramId}_{DateTime.UtcNow.Ticks}",
            IsAuthorized = false,
            IsTemporary = true,
            PreferredLanguage = BotLanguageCodes.English,
            Language = _localizationService.NormalizeDisplayName(BotLanguageCodes.English),
            PendingReferralCode = referralCode // ✅ Зберігаємо реферальний код
        };

        _context.Users.Add(user);
        await _context.SaveChangesAsync();

        return user;
    }
    
    public async Task<string> GetUserLanguageAsync(long chatId)
    {
        var user = await _context.Users.FirstOrDefaultAsync(u => u.TelegramId == chatId);
        return _localizationService.NormalizeDisplayName(user?.PreferredLanguage ?? user?.Language);
    }

    public async Task<User?> GetUserByTelegramIdAsync(long telegramId)
    {
        var user = await _context.Users.AsNoTracking().FirstOrDefaultAsync(u => u.TelegramId == telegramId);
        if (user == null)
        {
            return null;
        }

        user.PreferredLanguage = _localizationService.NormalizeCode(user.PreferredLanguage ?? user.Language);
        user.Language = _localizationService.NormalizeDisplayName(user.PreferredLanguage);
        return user;
    }

    public Task<User> AuthorizeUserAsync(long telegramId)
    {
        throw new NotImplementedException();
    }
    
    public async Task<bool> UpdateUserLanguageAsync(long telegramId, string newLanguage)
    {
        var user = await _context.Users.FirstOrDefaultAsync(u => u.TelegramId == telegramId);
        if (user == null)
            return false;

        user.PreferredLanguage = _localizationService.NormalizeCode(newLanguage);
        user.Language = _localizationService.NormalizeDisplayName(user.PreferredLanguage);
        user.UpdatedAt = DateTime.UtcNow;
        
        _context.Users.Update(user);
        await _context.SaveChangesAsync();
        
        return true;
    }

    public async Task<bool> UpdateUserLoginAsync(long telegramId, string newLogin)
    {
        // Перевіряємо, чи логін не зайнятий
        var existingUser = await _context.Users.FirstOrDefaultAsync(u => u.Username == newLogin);
        if (existingUser != null && existingUser.TelegramId != telegramId)
        {
            return false; // Логін вже зайнятий
        }

        var user = await _context.Users.FirstOrDefaultAsync(u => u.TelegramId == telegramId);
        if (user == null)
            return false;

        user.Username = newLogin;
        user.UpdatedAt = DateTime.UtcNow;
        
        _context.Users.Update(user);
        await _context.SaveChangesAsync();
        
        return true;
    }

    public async Task<bool> UpdateUserPasswordAsync(long telegramId, string newPassword)
    {
        var user = await _context.Users.FirstOrDefaultAsync(u => u.TelegramId == telegramId);
        if (user == null)
            return false;

        // Використовуємо вашу існуючу логіку хешування
        user.PasswordHash = HashPassword(newPassword);
        user.UpdatedAt = DateTime.UtcNow;
        
        _context.Users.Update(user);
        await _context.SaveChangesAsync();
        
        return true;
    }

    public async Task<bool> ValidateUserPasswordAsync(long telegramId, string password)
    {
        var user = await _context.Users.FirstOrDefaultAsync(u => u.TelegramId == telegramId);
        if (user == null)
            return false;

        // Використовуємо вашу існуючу логіку перевірки пароля
        var passwordHash = HashPassword(password);
        return user.PasswordHash == passwordHash;
    }

    public async Task<bool> IsLoginAvailableAsync(string login)
    {
        return !await _context.Users.AnyAsync(u => u.Username == login);
    }

    public async Task<bool> UpdateUserWalletAddressAsync(long chatId, string newWalletAddress)
    {
        try
        {
            // Перевірка вхідних даних
            if (string.IsNullOrWhiteSpace(newWalletAddress))
            {
                return false;
            }

            // Очищення адреси від зайвих пробілів
            newWalletAddress = newWalletAddress.Trim();

            // Базова валідація адреси гаманця
            if (!IsValidWalletAddress(newWalletAddress))
            {
                return false;
            }

            // Пошук користувача в базі даних
            var user = await _context.Users
                .FirstOrDefaultAsync(u => u. TelegramId == chatId);

            if (user == null)
            {
                return false;
            }

            // Перевірка, чи не використовується ця адреса вже іншим користувачем
            var existingUser = await _context.Users
                .FirstOrDefaultAsync(u => u.WalletAddress == newWalletAddress && u.TelegramId != chatId);

            if (existingUser != null)
            {
                return false; // Адреса вже використовується
            }

            // Оновлення адреси гаманця
            user.WalletAddress = newWalletAddress;
            user.UpdatedAt = DateTime.UtcNow;

            // Збереження змін
            await _context.SaveChangesAsync();

            return true;
        }
        catch (Exception ex)
        {
            // Логування помилки
            Console.WriteLine($"Error updating wallet address: {ex.Message}");
            return false;
        }
    }
    
    private bool IsValidWalletAddress(string walletAddress)
    {
        if (string.IsNullOrWhiteSpace(walletAddress))
            return false;

        // Мінімальна та максимальна довжина для більшості криптоадрес
        if (walletAddress.Length < 26 || walletAddress.Length > 64)
            return false;

        // Базові перевірки формату
        // BTC: починається з 1, 3, або bc1 (для segwit)
        // ETH: починається з 0x і має 42 символи
        // Інші: зазвичай містять тільки буквено-цифрові символи
    
        // Спрощена перевірка - можна розширити для конкретних блокчейнів
        return walletAddress.All(c => 
            char.IsLetterOrDigit(c) || 
            c == 'x' || 
            c == '1' || 
            c == '3' || 
            c == '0' || 
            c == 'q' || 
            c == 'p' || 
            c == 'b' || 
            c == 'c' ||
            c == '_' ||
            c == '-');
    }

    // Додаємо ваш метод хешування з AuthService
    private string HashPassword(string password)
    {
        using var sha256 = SHA256.Create();
        var bytes = Encoding.UTF8.GetBytes(password);
        var hash = sha256.ComputeHash(bytes);
        return Convert.ToBase64String(hash);
    }
}