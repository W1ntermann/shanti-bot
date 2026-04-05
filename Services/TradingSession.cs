using ShantiBotDi.Models;

namespace ShantiBotDi.Services;

public class TradingSession
{
    public long UserId { get; set; }
    public long TelegramId { get; set; }
    public List<CandleData> Candles { get; set; } = new();
    public List<Investment> ActiveInvestments { get; set; } = new();
    public decimal InitialBalance { get; set; }
    public decimal CurrentBalance { get; set; }
    public DateTime SessionStartTime { get; set; }
    public Random Random { get; set; } = new();
}