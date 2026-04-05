namespace ShantiBotDi.Services;

public interface IMaintenanceService
{
    Task NotifyMaintenanceStartAsync(string reason);
    Task NotifyMaintenanceEndAsync();
}