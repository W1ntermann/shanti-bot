namespace ShantiBotDi.Models;

public class WithdrawalRequest
{
    public int Id { get; set; }
    public long UserId { get; set; }
    public decimal Amount { get; set; }
    public string Status { get; set; } // Pending, Approved, Rejected
    public DateTime CreatedAt { get; set; }
    public User? User { get; set; }
    public bool IsFullWithdrawal { get; set; } = false; // Нова властивість
}