namespace ShantiBotDi.Services;

public interface ITopUserService
{
    Task<bool> IsTop100UserAsync(long userId);
}