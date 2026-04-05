namespace ShantiBotDi.Models;

public class Quest
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public decimal Reward { get; set; }
    public bool IsDaily { get; set; } = true;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    
    public string TargetType { get; set; } = "manual"; // manual, balance, investment, referral, etc.
    public decimal TargetValue { get; set; } = 1; // Скільки потрібно зробити (наприклад: 500 USDT)
    public string ProgressUnit { get; set; } = "times"; // times, USDT, referrals, etc.

    public virtual ICollection<UserQuest> UserQuests { get; set; } = new List<UserQuest>();
}