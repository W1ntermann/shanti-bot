namespace ShantiBotDi.Models;

public class Deposit
{
    public int Id { get; set; }
    
    public long UserId { get; set; }
    
    public decimal Amount { get; set; }
    
    public string Currency { get; set; } = string.Empty;
    
    public string Status { get; set; } = "Pending";
    
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? UpdatedAt { get; set; }
}