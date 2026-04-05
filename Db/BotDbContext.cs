using Microsoft.EntityFrameworkCore;
using ShantiBotDi.Models;

namespace ShantiBotDi.Db;

public class BotDbContext : DbContext
{
    public DbSet<User> Users { get; set; }
    public DbSet<Investment> Investments { get; set; }
    public DbSet<Deposit> Deposits { get; set; }
    public DbSet<WithdrawalRequest> WithdrawalRequests { get; set; }
    public DbSet<DepositRequest> DepositRequests { get; set; }

    public DbSet<Quest> Quests { get; set; }
    public DbSet<UserQuest> UserQuests { get; set; }

    public BotDbContext(DbContextOptions<BotDbContext> options) : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<User>().HasIndex(u => u.TelegramId).IsUnique();

        modelBuilder.Entity<Investment>()
            .HasIndex(i => i.UserId);

        modelBuilder.Entity<Investment>()
            .HasIndex(i => i.IsActive);

        modelBuilder.Entity<Investment>()
            .HasOne(i => i.User)
            .WithMany(u => u.Investments)
            .HasForeignKey(i => i.UserId);

        // Зв'язок User ↔ DepositRequest
        modelBuilder.Entity<DepositRequest>()
            .HasOne(dr => dr.User)
            .WithMany(u => u.DepositRequests)
            .HasForeignKey(dr => dr.UserId);
        modelBuilder.Entity<WithdrawalRequest>()
            .HasOne(wr => wr.User)
            .WithMany(u => u.WithdrawalRequests)
            .HasForeignKey(wr => wr.UserId);
        modelBuilder.Entity<User>()
            .HasIndex(u => u.ReferralCode)
            .IsUnique();

        modelBuilder.Entity<WithdrawalRequest>(entity =>
        {
            entity.HasIndex(wr => wr.UserId);
            entity.HasIndex(wr => wr.Status);
            entity.HasIndex(wr => wr.CreatedAt);
            entity.Property(wr => wr.IsFullWithdrawal)
                .HasDefaultValue(false); // Значення за замовчуванням
        });

        modelBuilder.Entity<Quest>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Title).IsRequired().HasMaxLength(200);
            entity.Property(e => e.Description).HasMaxLength(500);
            entity.Property(e => e.Reward).HasPrecision(18, 8);
            entity.Property(e => e.TargetValue).HasPrecision(18, 8);
            entity.Property(e => e.TargetType).HasMaxLength(50);
            entity.Property(e => e.ProgressUnit).HasMaxLength(50);
            
            // Відносини
            entity.HasMany(e => e.UserQuests)
                .WithOne(e => e.Quest)
                .HasForeignKey(e => e.QuestId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // Конфігурація для UserQuest
        modelBuilder.Entity<UserQuest>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.CurrentProgress).HasPrecision(18, 8);
            entity.Property(e => e.Date).IsRequired();
            
            // Відносини
            entity.HasOne(e => e.User)
                .WithMany()
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);
            
            entity.HasOne(e => e.Quest)
                .WithMany(e => e.UserQuests)
                .HasForeignKey(e => e.QuestId)
                .OnDelete(DeleteBehavior.Restrict);
            
            // Індекс для швидкого пошуку
            entity.HasIndex(e => new { e.UserId, e.QuestId, e.Date });
            entity.HasIndex(e => e.Date);
            entity.HasIndex(e => e.IsCompleted);
            entity.HasIndex(e => e.IsRewarded);
        });
    

        base.OnModelCreating(modelBuilder);
    }
}