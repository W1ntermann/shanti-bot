namespace ShantiBotDi.Services;

public interface ITradingSimulationService
{
    Task InitializeChartSessionAsync(long telegramId);
    Task<List<CandleData>> GenerateChartCandlesAsync(long telegramId);
    Task<TradingMetrics> GetChartMetricsAsync(long telegramId);
    void ClearSession(long telegramId);
}