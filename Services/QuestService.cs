using Microsoft.EntityFrameworkCore;
using ShantiBotDi.Db;
using ShantiBotDi.Models;
using Telegram.Bot;

namespace ShantiBotDi.Services;

public class QuestService : IQuestService
{
    private readonly BotDbContext _dbContext;
    private readonly ITelegramBotClient _botClient;

    public QuestService(BotDbContext dbContext, ITelegramBotClient botClient)
    {
        _dbContext = dbContext;
        _botClient = botClient;
    }

    public async Task<List<Quest>> GetDailyQuestsAsync()
    {
        return await _dbContext.Quests
            .Where(q => q.IsDaily && q.IsActive)
            .ToListAsync();
    }

    public async Task<List<UserQuest>> GetUserQuestsAsync(long telegramId)
    {
        var user = await _dbContext.Users
            .FirstOrDefaultAsync(u => u.TelegramId == telegramId);

        if (user == null)
        {
            Console.WriteLine($"❌ User not found for telegramId: {telegramId}");
            return new List<UserQuest>();
        }

        Console.WriteLine($"🔍 Getting quests for user {user.Id} (Telegram: {telegramId})");

        // Перевіряємо, чи є у користувача квести
        var hasAnyQuests = await _dbContext.UserQuests
            .AnyAsync(uq => uq.UserId == user.Id);

        Console.WriteLine($"🔍 User has any quests: {hasAnyQuests}");

        // Якщо немає жодного квеста - створюємо всі
        if (!hasAnyQuests)
        {
            Console.WriteLine($"🔍 Creating all quests for user {user.Id}");
            await AssignAllQuestsToUserAsync(user.Id);
        }

        var today = DateTime.UtcNow.Date;

        // Перевіряємо чи є сьогоднішні щоденні квести
        var hasTodayQuests = await _dbContext.UserQuests
            .AnyAsync(uq => uq.UserId == user.Id && uq.Date.Date == today && uq.Quest.IsDaily);

        Console.WriteLine($"🔍 User has today's daily quests: {hasTodayQuests}");

        if (!hasTodayQuests)
        {
            Console.WriteLine($"🔍 Assigning daily quests for today to user {user.Id}");
            await AssignDailyQuestsToUserAsync(user.Id);
        }

        // Отримуємо квести для показу
        var userQuests = await _dbContext.UserQuests
            .Where(uq => uq.UserId == user.Id && (uq.Date.Date == today || !uq.Quest.IsDaily))
            .Include(uq => uq.Quest)
            .OrderBy(uq => uq.Quest.IsDaily ? 0 : 1)
            .ThenBy(uq => uq.Quest.Reward)
            .ToListAsync();

        Console.WriteLine($"✅ Found {userQuests.Count} quests for user {user.Id}");

        return userQuests;
    }

    public async Task<(bool success, decimal reward)> ClaimQuestRewardAsync(long telegramId, int questId)
    {
        var user = await _dbContext.Users
            .FirstOrDefaultAsync(u => u.TelegramId == telegramId);

        if (user == null)
        {
            Console.WriteLine($"❌ User not found for claim: {telegramId}");
            return (false, 0);
        }

        var userQuest = await _dbContext.UserQuests
            .Include(uq => uq.Quest)
            .FirstOrDefaultAsync(uq => uq.UserId == user.Id && uq.QuestId == questId);

        if (userQuest == null)
        {
            Console.WriteLine($"❌ Quest {questId} not found for user {user.Id}");
            return (false, 0);
        }

        if (!userQuest.IsCompleted)
        {
            Console.WriteLine($"❌ Quest {questId} not completed for user {user.Id}");
            return (false, 0);
        }

        if (userQuest.IsRewarded)
        {
            Console.WriteLine($"❌ Quest {questId} already rewarded for user {user.Id}");
            return (false, 0);
        }

        // ✅ Автоматично додаємо нагороду в баланс
        user.Balance += userQuest.Quest.Reward;
        userQuest.IsRewarded = true;
        userQuest.RewardedAt = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync();

        Console.WriteLine($"✅ User {user.Id} claimed {userQuest.Quest.Reward} USDT for quest {questId}");
        return (true, userQuest.Quest.Reward);
    }

    public async Task<bool> UpdateQuestProgressAsync(long telegramId, int questId, decimal progressToAdd = 1)
    {
        Console.WriteLine($"📊 UpdateQuestProgressAsync: user {telegramId}, quest {questId}, add {progressToAdd}");

        try
        {
            var user = await _dbContext.Users
                .FirstOrDefaultAsync(u => u.TelegramId == telegramId);

            if (user == null)
            {
                Console.WriteLine($"❌ User {telegramId} not found");
                return false;
            }

            var userQuest = await _dbContext.UserQuests
                .Include(uq => uq.Quest)
                .FirstOrDefaultAsync(uq => uq.UserId == user.Id && uq.QuestId == questId);

            if (userQuest == null)
            {
                Console.WriteLine($"❌ Quest {questId} not found for user {telegramId}");
                return false;
            }

            if (userQuest.IsCompleted)
            {
                Console.WriteLine($"⚠️ Quest {questId} already completed");
                return false;
            }

            // Оновлюємо прогрес, але не більше ніж TargetValue
            var newProgress = userQuest.CurrentProgress + progressToAdd;
            userQuest.CurrentProgress = Math.Min(newProgress, userQuest.Quest.TargetValue);

            // Перевіряємо чи квест виконаний
            if (userQuest.CurrentProgress >= userQuest.Quest.TargetValue && !userQuest.IsCompleted)
            {
                userQuest.IsCompleted = true;
                userQuest.CompletedAt = DateTime.UtcNow;
                Console.WriteLine($"✅ Quest {questId} completed for user {telegramId}");
            }

            await _dbContext.SaveChangesAsync();
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Error in UpdateQuestProgressAsync: {ex.Message}");
            return false;
        }
    }

    public async Task UpdateUserProgressAutomaticallyAsync(long telegramId)
    {
        Console.WriteLine($"🔄 UpdateUserProgressAutomaticallyAsync: user {telegramId}");

        try
        {
            var user = await _dbContext.Users
                .Include(u => u.Investments)
                .FirstOrDefaultAsync(u => u.TelegramId == telegramId);

            if (user == null)
            {
                Console.WriteLine($"❌ User {telegramId} not found");
                return;
            }

            var userQuests = await _dbContext.UserQuests
                .Include(uq => uq.Quest)
                .Where(uq => uq.UserId == user.Id && !uq.IsCompleted)
                .ToListAsync();

            Console.WriteLine($"📝 Processing {userQuests.Count} quests for user {telegramId}");

            foreach (var userQuest in userQuests)
            {
                Console.WriteLine($"   Quest: {userQuest.Quest?.Title} (Type: {userQuest.Quest?.TargetType})");

                switch (userQuest.Quest?.TargetType)
                {
                    case "balance":
                        // Обмежуємо прогрес до TargetValue
                        userQuest.CurrentProgress = Math.Min(user.Balance, userQuest.Quest.TargetValue);
                        if (user.Balance >= userQuest.Quest.TargetValue)
                        {
                            userQuest.IsCompleted = true;
                            userQuest.CompletedAt = DateTime.UtcNow;
                            Console.WriteLine($"   ✅ Balance quest completed: {user.Balance} USDT");
                        }

                        break;

                    case "investment":
                        var investmentCount = user.Investments.Count(i => i.IsActive);
                        // Обмежуємо прогрес до TargetValue
                        userQuest.CurrentProgress = Math.Min(investmentCount, userQuest.Quest.TargetValue);
                        if (investmentCount >= userQuest.Quest.TargetValue)
                        {
                            userQuest.IsCompleted = true;
                            userQuest.CompletedAt = DateTime.UtcNow;
                            Console.WriteLine($"   ✅ Investment quest completed: {investmentCount} active investments");
                        }

                        break;

                    case "profit":
                        var totalProfit = user.Investments
                            .Where(i => i.IsActive)
                            .Sum(i => i.AccumulatedProfit);

                        // Обмежуємо прогрес до TargetValue
                        userQuest.CurrentProgress = Math.Min(totalProfit, userQuest.Quest.TargetValue);

                        if (totalProfit >= userQuest.Quest.TargetValue)
                        {
                            userQuest.IsCompleted = true;
                            userQuest.CompletedAt = DateTime.UtcNow;
                            Console.WriteLine($"   ✅ Profit quest completed: {totalProfit} USDT");
                        }

                        break;

                    case "referrals":
                        var referralCount = await _dbContext.Users
                            .CountAsync(u => u.ReferredBy == user.Id);

                        // Обмежуємо прогрес до TargetValue
                        userQuest.CurrentProgress = Math.Min(referralCount, userQuest.Quest.TargetValue);

                        if (referralCount >= userQuest.Quest.TargetValue)
                        {
                            userQuest.IsCompleted = true;
                            userQuest.CompletedAt = DateTime.UtcNow;
                            Console.WriteLine($"   ✅ Referral quest completed: {referralCount} referrals");
                        }

                        break;

                    case "deposits":
                        var depositCount = await _dbContext.DepositRequests
                            .CountAsync(d => d.UserId == user.Id && d.Status == "Approved");

                        // Обмежуємо прогрес до TargetValue
                        userQuest.CurrentProgress = Math.Min(depositCount, userQuest.Quest.TargetValue);

                        if (depositCount >= userQuest.Quest.TargetValue)
                        {
                            userQuest.IsCompleted = true;
                            userQuest.CompletedAt = DateTime.UtcNow;
                            Console.WriteLine($"   ✅ Deposit quest completed: {depositCount} deposits");
                        }

                        break;

                    case "manual":
                        // Manual квести не оновлюються автоматично
                        Console.WriteLine($"   ⏭️ Manual quest - no automatic update");
                        break;

                    default:
                        Console.WriteLine($"   ❓ Unknown TargetType: {userQuest.Quest?.TargetType}");
                        break;
                }
            }

            var changes = await _dbContext.SaveChangesAsync();
            Console.WriteLine($"✅ UpdateUserProgressAutomaticallyAsync completed: {changes} changes saved");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Error in UpdateUserProgressAutomaticallyAsync: {ex.Message}");
        }
    }

    // НОВІ МЕТОДИ:

    private async Task AssignAllQuestsToUserAsync(long userId)
    {
        var today = DateTime.UtcNow.Date;

        // Отримуємо всі активні квести
        var allQuests = await _dbContext.Quests
            .Where(q => q.IsActive)
            .ToListAsync();

        Console.WriteLine($"🔍 Found {allQuests.Count} active quests to assign to user {userId}");

        var userQuestsToAdd = new List<UserQuest>();

        foreach (var quest in allQuests)
        {
            // Для щоденних квестів - на сьогодні
            // Для нещоденних - на поточну дату (вони не оновлюються)
            var questDate = quest.IsDaily ? today : today;

            userQuestsToAdd.Add(new UserQuest
            {
                UserId = userId,
                QuestId = quest.Id,
                Date = questDate,
                IsCompleted = false,
                IsRewarded = false,
                CurrentProgress = 0,
                CreatedAt = DateTime.UtcNow
            });
        }

        if (userQuestsToAdd.Any())
        {
            await _dbContext.UserQuests.AddRangeAsync(userQuestsToAdd);
            await _dbContext.SaveChangesAsync();

            Console.WriteLine($"✅ Призначено {userQuestsToAdd.Count} квестів для користувача {userId}");
        }
    }

    private async Task AssignDailyQuestsToUserAsync(long userId)
    {
        var today = DateTime.UtcNow.Date;

        // Видаляємо старі щоденні квести (якщо вони залишилися з минулого дня)
        var oldDailyQuests = await _dbContext.UserQuests
            .Include(uq => uq.Quest)
            .Where(uq => uq.UserId == userId && uq.Quest.IsDaily && uq.Date.Date < today)
            .ToListAsync();

        if (oldDailyQuests.Any())
        {
            _dbContext.UserQuests.RemoveRange(oldDailyQuests);
            Console.WriteLine($"🗑️ Removed {oldDailyQuests.Count} old daily quests for user {userId}");
        }

        // Отримуємо щоденні квести
        var dailyQuests = await _dbContext.Quests
            .Where(q => q.IsDaily && q.IsActive)
            .ToListAsync();

        Console.WriteLine($"🔍 Found {dailyQuests.Count} daily quests to assign");

        foreach (var quest in dailyQuests)
        {
            // Перевіряємо, чи вже є такий квест на сьогодні
            var existingQuest = await _dbContext.UserQuests
                .AnyAsync(uq => uq.UserId == userId && uq.QuestId == quest.Id && uq.Date.Date == today);

            if (!existingQuest)
            {
                var userQuest = new UserQuest
                {
                    UserId = userId,
                    QuestId = quest.Id,
                    Date = today,
                    IsCompleted = false,
                    IsRewarded = false,
                    CurrentProgress = 0,
                    CreatedAt = DateTime.UtcNow
                };

                _dbContext.UserQuests.Add(userQuest);
            }
        }

        await _dbContext.SaveChangesAsync();
        Console.WriteLine($"✅ Щоденні квести оновлені для користувача {userId}");
    }

    public async Task ResetDailyQuestsForAllUsersAsync()
    {
        try
        {
            var today = DateTime.UtcNow.Date;
            var yesterday = today.AddDays(-1);

            Console.WriteLine($"🔄 Resetting daily quests for all users. Today: {today}, Yesterday: {yesterday}");

            // Видаляємо вчорашні щоденні квести
            var oldDailyQuests = await _dbContext.UserQuests
                .Include(uq => uq.Quest)
                .Where(uq => uq.Quest.IsDaily && uq.Date.Date < today)
                .ToListAsync();

            if (oldDailyQuests.Any())
            {
                _dbContext.UserQuests.RemoveRange(oldDailyQuests);
                await _dbContext.SaveChangesAsync();
                Console.WriteLine($"🗑️ Видалено {oldDailyQuests.Count} старих щоденних квестів");
            }

            // Отримуємо всіх активних користувачів
            var allUsers = await _dbContext.Users
                .Where(u => u.IsAuthorized)
                .ToListAsync();

            var dailyQuests = await _dbContext.Quests
                .Where(q => q.IsDaily && q.IsActive)
                .ToListAsync();

            Console.WriteLine($"🔍 Found {allUsers.Count} users and {dailyQuests.Count} daily quests");

            foreach (var user in allUsers)
            {
                foreach (var quest in dailyQuests)
                {
                    // Перевіряємо, чи вже є квест на сьогодні
                    var existingQuest = await _dbContext.UserQuests
                        .AnyAsync(uq => uq.UserId == user.Id &&
                                        uq.QuestId == quest.Id &&
                                        uq.Date.Date == today);

                    if (!existingQuest)
                    {
                        _dbContext.UserQuests.Add(new UserQuest
                        {
                            UserId = user.Id,
                            QuestId = quest.Id,
                            Date = today,
                            IsCompleted = false,
                            IsRewarded = false,
                            CurrentProgress = 0,
                            CreatedAt = DateTime.UtcNow
                        });
                    }
                }
            }

            await _dbContext.SaveChangesAsync();
            Console.WriteLine($"✅ Щоденні квести скинуті для {allUsers.Count} користувачів");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Помилка скидання щоденних квестів: {ex.Message}");
        }
    }
}