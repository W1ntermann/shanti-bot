namespace ShantiBotDi.Models;

public class Investment
{
    public long Id { get; set; }
    public long UserId { get; set; }
    public decimal Amount { get; set; }
    public InvestmentDuration Duration { get; set; }
    public decimal InterestPercent { get; set; }
    public bool IsActive { get; set; } = true;
    public decimal AccumulatedProfit { get; set; }
    public virtual User User { get; set; }
    public DateTime StartDate { get; set; } = DateTime.UtcNow;
    public DateTime EndDate { get; set; }
    public int DurationHours { get; set; }
    public bool ProfitAddedToBalance { get; set; } = false;
}