namespace ShantiBotDi.Services;

public interface IRegisterService
{
    Task<(bool Success, string Message)> RegisterUserAsync(long telegramId, 
        string username, string password, string walletAddress, string language,  string? referralCode = null);
}