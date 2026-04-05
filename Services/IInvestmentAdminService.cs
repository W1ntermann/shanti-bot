namespace ShantiBotDi.Services;

public interface IInvestmentAdminService
{
    Task ShowAllUsersSummaryAsync(long adminChatId);
    Task ShowUserInvestmentsAsync(long adminChatId, long targetTelegramId);
}