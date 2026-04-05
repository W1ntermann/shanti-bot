namespace ShantiBotDi.Services;

public interface ILoggerService
{
    void LogInformation(string message);
    void LogInformation(string message, params object[] args);
    void LogWarning(string message);
    void LogWarning(string message, params object[] args);
    void LogError(string message);
    void LogError(string message, params object[] args);
    void LogError(Exception exception, string message);
    void LogError(Exception exception, string message, params object[] args);
    void LogDebug(string message);
    void LogDebug(string message, params object[] args);
}