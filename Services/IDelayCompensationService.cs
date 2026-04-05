namespace ShantiBotDi.Services;

public interface IDelayCompensationService
{
    Task NotifyDelayAndCompensateAsync();
    Task NotifyDelayAndCompensateWithCustomReasonAsync(string customReason);
    Task<int> GetAffectedUsersCountAsync();
}