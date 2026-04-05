namespace ShantiBotDi.Services;

public interface IProfileService
{
    Task ShowProfileAsync(long chatId, bool skipInvestmentCheck, int? messageId = null);
    Task<decimal> GetBalanceAsync(long chatId);
    Task<decimal> GetTotalInvestedAsync(long telegramId);
    //Task UpdateInvestmentsInRealTime(long chatId);
    Task CheckInvestmentsForUser(long chatId);

}