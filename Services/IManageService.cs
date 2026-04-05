using ShantiBotDi.Models;

namespace ShantiBotDi;
    
public interface IManageService
{
    Task<List<User>> GetUsersAsync();
    Task<User> RegisterUserIfNotExistsAsync(long telegramId, string? username, long? referredBy = null);
    Task<User?> GetUserByTelegramIdAsync(long telegramId);
    Task<User> AuthorizeUserAsync(long telegramId);
    Task<User?> FindOrCreateTempUserAsync(long telegramId, string? username, string? referralCode = null);
    //Task UpdateUserLanguageAsync(long telegramId, string language);
    Task<string> GetUserLanguageAsync(long chatId);
    Task<bool> UpdateUserLanguageAsync(long telegramId, string newLanguage);
    Task<bool> UpdateUserLoginAsync(long telegramId, string newLogin);
    Task<bool> UpdateUserPasswordAsync(long telegramId, string newPassword);
    Task<bool> ValidateUserPasswordAsync(long telegramId, string password);
    Task<bool> IsLoginAvailableAsync(string login);
    Task<bool> UpdateUserWalletAddressAsync(long chatId, string newWalletAddress);

}