namespace ShantiBotDi.Services;

public class TradingMetrics
{
    public decimal CurrentPrice { get; set; }
    public decimal HighPrice { get; set; }
    public decimal LowPrice { get; set; }
    public decimal ChangePercent { get; set; }
    public decimal BalanceChangePercent { get; set; }
    public decimal ProfitLoss { get; set; }
    public int CandleCount { get; set; }
}