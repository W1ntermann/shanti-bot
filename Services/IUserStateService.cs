using ShantiBotDi.Models;

namespace ShantiBotDi.Services;

public interface IUserStateService
{
    Task SetStateAsync(long userId, UserAction action);
    Task<UserAction> GetStateAsync(long userId);
    Task ClearStateAsync(long userId);
    Task<User> GetByChatIdAsync(long chatId);
    Task<int> GetReferralCountAsync(long telegramId);

    Task ClearTempUsernameAsync(long userId);
    Task<string?> GetTempUsernameAsync(long userId);
    Task SetTempUsernameAsync(long userId, string tempUsername);
}