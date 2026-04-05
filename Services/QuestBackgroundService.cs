namespace ShantiBotDi.Services;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

public class QuestBackgroundService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<QuestBackgroundService> _logger;

    public QuestBackgroundService(IServiceProvider serviceProvider, ILogger<QuestBackgroundService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("🚀 QuestBackgroundService запущений");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var now = DateTime.UtcNow;
                
                // Виконуємо о 00:00 UTC кожного дня
                if (now.Hour == 0 && now.Minute == 0)
                {
                    using var scope = _serviceProvider.CreateScope();
                    var questService = scope.ServiceProvider.GetRequiredService<IQuestService>();
                    
                    if (questService is QuestService concreteQuestService)
                    {
                        await concreteQuestService.ResetDailyQuestsForAllUsersAsync();
                        _logger.LogInformation("✅ Щоденні квести скинуті");
                    }
                }

                // Перевіряємо кожну хвилину
                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Помилка в QuestBackgroundService");
                await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
            }
        }
    }
}