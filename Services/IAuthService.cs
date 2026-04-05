namespace ShantiBotDi.Services;

public interface IAuthService
{
    Task<bool> RegisterAsync(long telegramId, string username, string password);
    Task<string> LoginAsync(long telegramId, string password);
    Task<bool> IsAuthorizedAsync(long telegramId, string token);
    Task LogoutAsync(long telegramId);
    Task<bool> ValidateLoginAsync(string username, string password);
    string HashPassword(string password);
}