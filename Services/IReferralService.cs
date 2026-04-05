namespace ShantiBotDi.Services;

public interface IReferralService
{
    Task<decimal> GetTotalReferralRewardsAsync(long telegramId);
    Task<(int referralCount, decimal referralReward)> GetReferralSummaryAsync(long telegramId);
    Task<decimal> GetExpectedReferralProfitAsync(long chatId);
    Task<decimal> GetTotalReferralProfitAsync(long chatId);
    decimal GetUserReferralPercentage(long userId);
    Task<bool> Set25PercentBonusAsync(long userId, bool enable);
}