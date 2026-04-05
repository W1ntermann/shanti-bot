namespace ShantiBotDi.Models;

public class User
{
    public long Id { get; set; }
    public string Username { get; set; } =  string.Empty;
    public decimal Deposit { get; set; }
    public decimal Balance { get; set; }
    public DateTime RegistrationDate { get; set; } = DateTime.UtcNow;
    public long TelegramId { get; set; }
    public UserAction State { get; set; } = UserAction.None;
    public string ReferralCode { get; set; } = Guid.NewGuid().ToString("N").Substring(0, 8).ToLower();
    public long? ReferredBy { get; set; }
    public DateTime? LastInvestmentUpdate { get; set; }
    public virtual ICollection<Investment> Investments { get; set; } = new List<Investment>();
    public ICollection<DepositRequest> DepositRequests { get; set; } = new List<DepositRequest>();
    public ICollection<WithdrawalRequest> WithdrawalRequests { get; set; } = new List<WithdrawalRequest>();
    public UserRole Role { get; set; } = UserRole.User;
    public string? PasswordHash { get; set; }
    public bool IsAuthorized { get; set; } = false;
    public string? SessionToken { get; set; }
    public DateTime TokenExpiry { get; set; }
    public string? TempUsername { get; set; }
    public string? WalletAddress { get; set; }
    public string PreferredLanguage { get; set; } = "en"; // en = English за замовчуванням
    public string? Language { get; set; } = "English";
    public bool HasReadRules { get; set; }
    public bool IsTemporary {get; set;} =  false;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    
    public string? PendingReferralCode { get; set; }
    public decimal TotalReferralRewards { get; set; }
    public bool Has25PercentBonus { get; set; } = false;
    public List<UserQuest> Quests { get; set; } = new List<UserQuest>();
}