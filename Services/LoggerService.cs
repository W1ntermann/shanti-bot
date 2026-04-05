using Microsoft.Extensions.Logging;

namespace ShantiBotDi.Services;

public class LoggerService : ILoggerService
{
    private readonly ILogger<LoggerService> _logger;

    public LoggerService(ILogger<LoggerService> logger)
    {
        _logger = logger;
    }

    public void LogInformation(string message)
    {
        _logger.LogInformation(message);
    }

    public void LogInformation(string message, params object[] args)
    {
        _logger.LogInformation(message, args);
    }

    public void LogWarning(string message)
    {
        _logger.LogWarning(message);
    }

    public void LogWarning(string message, params object[] args)
    {
        _logger.LogWarning(message, args);
    }

    public void LogError(string message)
    {
        _logger.LogError(message);
    }

    public void LogError(string message, params object[] args)
    {
        _logger.LogError(message, args);
    }

    public void LogError(Exception exception, string message)
    {
        _logger.LogError(exception, message);
    }

    public void LogError(Exception exception, string message, params object[] args)
    {
        _logger.LogError(exception, message, args);
    }

    public void LogDebug(string message)
    {
        _logger.LogDebug(message);
    }

    public void LogDebug(string message, params object[] args)
    {
        _logger.LogDebug(message, args);
    }
}