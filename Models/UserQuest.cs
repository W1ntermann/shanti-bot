namespace ShantiBotDi.Models;

public class UserQuest
{
    public int Id { get; set; }
    public long UserId { get; set; }
    public int QuestId { get; set; }
    public DateTime Date { get; set; }
    public bool IsCompleted { get; set; }
    public bool IsRewarded { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }
    public DateTime? RewardedAt { get; set; }
    
    // 🔴 НОВЕ ПОЛЕ ДЛЯ ПРОГРЕСУ
    public decimal CurrentProgress { get; set; } = 0;

    public virtual User User { get; set; }
    public virtual Quest Quest { get; set; }
}