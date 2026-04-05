using Microsoft.EntityFrameworkCore;
using ShantiBotDi.Db;
using ShantiBotDi.Models;

namespace ShantiBotDi.Services;

public class QuestManagementService : IQuestManagementService
{
    private readonly BotDbContext _dbContext;

    public QuestManagementService(BotDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task InitializeDailyQuestsAsync()
    {
        if (await _dbContext.Quests.AnyAsync())
        {
            Console.WriteLine("ℹ️ Quests already exist in database");
            return;
        }

        var defaultQuests = new List<Quest>
        {
            // DAILY QUESTS
            new()
            {
                Title = "Daily Check-in",
                Description = "Check in to the bot every day",
                Reward = 0.005m,
                IsDaily = true,
                IsActive = true,
                TargetType = "manual",
                TargetValue = 1,
                ProgressUnit = "times"
            },
            new()
            {
                Title = "Daily Activity",
                Description = "Perform any action in the bot",
                Reward = 0.008m,
                IsDaily = true,
                IsActive = true,
                TargetType = "manual",
                TargetValue = 1,
                ProgressUnit = "times"
            },

            // GENERAL QUESTS
            new()
            {
                Title = "First Deposit",
                Description = "Make your first deposit",
                Reward = 0.25m,
                IsDaily = false,
                IsActive = true,
                TargetType = "deposits",
                TargetValue = 1,
                ProgressUnit = "deposits"
            },
            new()
            {
                Title = "Deposit Master",
                Description = "Make 5 deposits",
                Reward = 1.0m,
                IsDaily = false,
                IsActive = true,
                TargetType = "deposits",
                TargetValue = 5,
                ProgressUnit = "deposits"
            },
            new()
            {
                Title = "Referral Starter",
                Description = "Invite your first friend",
                Reward = 0.25m,
                IsDaily = false,
                IsActive = true,
                TargetType = "referrals",
                TargetValue = 1,
                ProgressUnit = "friends"
            },
            new()
            {
                Title = "Referral Master",
                Description = "Invite 5 friends to the bot",
                Reward = 1.25m,
                IsDaily = false,
                IsActive = true,
                TargetType = "referrals",
                TargetValue = 5,
                ProgressUnit = "friends"
            },
            new()
            {
                Title = "First Investment",
                Description = "Make your first investment",
                Reward = 0.25m,
                IsDaily = false,
                IsActive = true,
                TargetType = "investment",
                TargetValue = 1,
                ProgressUnit = "investments"
            },
            new()
            {
                Title = "Active Investor",
                Description = "Have 3 active investments at the same time",
                Reward = 0.75m,
                IsDaily = false,
                IsActive = true,
                TargetType = "investment",
                TargetValue = 3,
                ProgressUnit = "investments"
            }
        };

        _dbContext.Quests.AddRange(defaultQuests);
        await _dbContext.SaveChangesAsync();

        Console.WriteLine($"✅ Initialized {defaultQuests.Count} quests");
    }

    public async Task<List<Quest>> GetAllQuestsAsync()
    {
        return await _dbContext.Quests
            .Where(q => q.IsActive)
            .OrderByDescending(q => q.IsDaily)
            .ThenByDescending(q => q.Reward)
            .ToListAsync();
    }

    public async Task<Quest> CreateQuestAsync(string title, string description, decimal reward, bool isDaily = true)
    {
        var quest = new Quest
        {
            Title = title.Trim(),
            Description = description?.Trim() ?? "",
            Reward = reward,
            IsDaily = isDaily,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            TargetType = "manual",
            TargetValue = 1,
            ProgressUnit = "times"
        };

        _dbContext.Quests.Add(quest);
        await _dbContext.SaveChangesAsync();
        
        Console.WriteLine($"✅ Created quest: {title}");
        return quest;
    }

    public async Task<bool> DeleteQuestAsync(int questId)
    {
        var quest = await _dbContext.Quests.FindAsync(questId);
        if (quest == null) return false;

        _dbContext.Quests.Remove(quest);
        await _dbContext.SaveChangesAsync();
        
        Console.WriteLine($"🗑️ Deleted quest: {quest.Title}");
        return true;
    }

    public async Task<bool> UpdateQuestAsync(int questId, string title, string description, decimal reward)
    {
        var quest = await _dbContext.Quests.FindAsync(questId);
        if (quest == null) return false;

        quest.Title = title.Trim();
        quest.Description = description?.Trim() ?? "";
        quest.Reward = reward;

        await _dbContext.SaveChangesAsync();
        
        Console.WriteLine($"✏️ Updated quest: {title}");
        return true;
    }

    public async Task<bool> DeactivateQuestAsync(int questId)
    {
        var quest = await _dbContext.Quests.FindAsync(questId);
        if (quest == null) return false;

        quest.IsActive = false;
        await _dbContext.SaveChangesAsync();
        
        Console.WriteLine($"🚫 Deactivated quest: {quest.Title}");
        return true;
    }
}