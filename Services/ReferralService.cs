using Microsoft.EntityFrameworkCore;
using ShantiBotDi.Db;

namespace ShantiBotDi.Services;

public class ReferralService : IReferralService
{
    private readonly BotDbContext _db;
    private readonly ITopUserService _topUserService;
    private const decimal DefaultPercentage = 0.10m;
    private const decimal BonusPercentage = 0.25m;

    public ReferralService(BotDbContext db, ITopUserService topUserService)
    {
        _db = db;
        _topUserService = topUserService;
    }

    public async Task<decimal> GetTotalReferralRewardsAsync(long telegramId)
    {
        var user = await _db.Users
            .FirstOrDefaultAsync(u => u.TelegramId == telegramId);

        return user?.TotalReferralRewards ?? 0m;
    }

    public async Task<(int referralCount, decimal referralReward)> GetReferralSummaryAsync(long telegramId)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.TelegramId == telegramId);
        if (user == null) return (0, 0m);

        // Отримуємо всіх рефералів
        var referrals = await _db.Users
            .Where(u => u.ReferredBy == user.Id)
            .ToListAsync();

        int referralCount = referrals.Count;

        // Отримуємо ID рефералів для пошуку інвестицій
        var referralIds = referrals.Select(r => r.Id).ToList();

        decimal totalProfitFromReferrals = 0m;

        if (referralIds.Any())
        {
            totalProfitFromReferrals = await _db.Investments
                .Where(i => referralIds.Contains(i.UserId) && !i.IsActive)
                .SumAsync(i => (decimal?)(i.Amount * i.InterestPercent / 100m)) ?? 0m;
        }

        // Використовуємо індивідуальний відсоток користувача
        decimal userPercentage = GetUserReferralPercentage(user.Id);
        decimal referralReward = totalProfitFromReferrals * userPercentage;

        return (referralCount, referralReward);
    }

    public async Task<decimal> GetTotalReferralProfitAsync(long chatId)
    {
        try
        {
            var user = await _db.Users.FirstOrDefaultAsync(u => u.TelegramId == chatId);
            if (user == null) return 0m;

            // Отримуємо ID всіх рефералів
            var referralIds = await _db.Users
                .Where(u => u.ReferredBy == user.Id)
                .Select(u => u.Id)
                .ToListAsync();

            if (!referralIds.Any()) return 0m;

            // Отримуємо загальний прибуток від активних інвестицій рефералів (БЕЗ відсотка)
            decimal? totalProfitNullable = await _db.Investments
                .Where(i => referralIds.Contains(i.UserId) && i.IsActive)
                .SumAsync(i => (decimal?)(i.Amount * i.InterestPercent / 100m));

            return totalProfitNullable ?? 0m;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error in GetTotalReferralProfitAsync: {ex.Message}");
            return 0m;
        }
    }

    public async Task<decimal> GetExpectedReferralProfitAsync(long chatId)
    {
        try
        {
            var user = await _db.Users.FirstOrDefaultAsync(u => u.TelegramId == chatId);
            if (user == null) return 0m;

            // Отримуємо ID всіх рефералів
            var referralIds = await _db.Users
                .Where(u => u.ReferredBy == user.Id)
                .Select(u => u.Id)
                .ToListAsync();

            if (!referralIds.Any()) return 0m;

            // Отримуємо загальний очікуваний прибуток від активних інвестицій рефералів
            decimal? expectedProfitNullable = await _db.Investments
                .Where(i => referralIds.Contains(i.UserId) && i.IsActive)
                .SumAsync(i => (decimal?)(i.Amount * i.InterestPercent / 100m));

            decimal expectedProfit = expectedProfitNullable ?? 0m;

            // Використовуємо індивідуальний відсоток користувача
            decimal userPercentage = GetUserReferralPercentage(user.Id);
            return expectedProfit * userPercentage;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error in GetExpectedReferralProfitAsync: {ex.Message}");
            return 0m;
        }
    }
    
    public decimal GetUserReferralPercentage(long userId)
    {
        var user = _db.Users.Find(userId);
        return user?.Has25PercentBonus == true ? BonusPercentage : DefaultPercentage;
    }

    public async Task<bool> Set25PercentBonusAsync(long userId, bool enable)
    {
        var user = await _db.Users.FindAsync(userId);
        if (user == null) return false;
        user.Has25PercentBonus = enable;
        await _db.SaveChangesAsync();
        return true;
    }
}