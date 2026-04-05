using System.Text;
using Microsoft.EntityFrameworkCore;
using ShantiBotDi.Db;
using ShantiBotDi.Models;
using Telegram.Bot;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace ShantiBotDi.Services;

public class ProfileService : IProfileService
{
    private readonly ITelegramBotClient _botClient;
    private readonly IManageService _manageService;
    private readonly BotDbContext _dbContext;
    private readonly IUserStateService _userStateService;
    private readonly IReferralService _referralService;
    private readonly ITopUserService _topUserService;
    private readonly Func<long, Task>? _showMainMenuAsync;

    public ProfileService(ITelegramBotClient botClient, IManageService manageService, BotDbContext dbContext,
        IUserStateService userStateService, IReferralService referralService, ITopUserService topUserService,
        Func<long, Task>? showMainMenuAsync = null)
    {
        _botClient = botClient;
        _manageService = manageService;
        _dbContext = dbContext;
        _userStateService = userStateService;
        _referralService = referralService;
        _topUserService = topUserService;
        _showMainMenuAsync = showMainMenuAsync;
    }

    public async Task ShowProfileAsync(long chatId, bool skipInvestmentCheck = false, int? messageId = null)
    {
        if (_manageService == null || _referralService == null || _topUserService == null)
        {
            await _botClient.SendMessage(chatId, "Service temporarily unavailable");
            return;
        }

        if (!skipInvestmentCheck)
        {
            await CheckAndNotifyCompletedInvestments(chatId, true);
        }

        var user = await _manageService.GetUserByTelegramIdAsync(chatId);
        if (user == null)
        {
            string notFoundMsg = (user?.Language ?? "English") == "English"
                ? "❗️Profile not found. Try again later or register."
                : "❗️Profile nahi mila. Phir se koshish karein ya register karein.";
            await _botClient.SendMessage(chatId, notFoundMsg);
            return;
        }

        var lang = user.Language ?? "English";

        // Отримуємо дані про інвестиції
        var hourlyProfit = await GetHourlyProfitAsync(chatId);
        var earnedPerHour = hourlyProfit;
        var earnedPerDay = hourlyProfit * 24;
        var earnedPerWeek = hourlyProfit * 24 * 7;

        var totalInvested = await GetTotalInvestedAsync(chatId);
        var accumulatedProfit = await GetAccumulatedProfitAsync(chatId);

        // Отримуємо кількість рефералів через ReferralService
        var referralSummary = await _referralService.GetReferralSummaryAsync(chatId);
        var referralsCount = referralSummary.referralCount;

        // Отримуємо загальні реферальні винагороди
        var totalReferralRewards = await _referralService.GetTotalReferralRewardsAsync(chatId);

        // Отримуємо очікуваний прибуток від рефералів (ВЖЕ включає 10%)
        var expectedReferralReward = await _referralService.GetExpectedReferralProfitAsync(chatId);

        // Отримуємо загальний прибуток рефералів (без 10%)
        var totalReferralProfit = await _referralService.GetTotalReferralProfitAsync(chatId);

        var activeInvestments = await _dbContext.Investments
            .Where(i => i.UserId == user.Id && i.IsActive)
            .ToListAsync();

        // Формуємо текст профілю
        var profileText = new StringBuilder();

        // Заголовок
        profileText.AppendLine(lang == "English"
            ? $"🙋‍♂️ <b>My Profile</b>\n"
            : $"🙋‍♂️ <b>Mera Profile</b>\n");

        // Основна інформація про баланси
        profileText.AppendLine(lang == "English"
            ? $"💰 <b>Current Balance:</b> {user.Balance:N2} USDT"
            : $"💰 <b>Current Balance:</b> {user.Balance:N2} USDT");

        profileText.AppendLine(lang == "English"
            ? $"💼 <b>Total invested:</b> {totalInvested:N2} USDT"
            : $"💼 <b>Total invested:</b> {totalInvested:N2} USDT");

        profileText.AppendLine(lang == "English"
            ? $"📈 <b>Accumulated Profit:</b> {accumulatedProfit:N2} USDT"
            : $"📈 <b>Accumulated Profit:</b> {accumulatedProfit:N2} USDT");

        profileText.AppendLine(lang == "English"
            ? $"🎯 <b>Referral Rewards:</b> {totalReferralRewards:N2} USDT"
            : $"🎯 <b>Referral Rewards:</b> {totalReferralRewards:N2} USDT");

        // Прогнозований дохід
        profileText.AppendLine("\n" + (lang == "English"
            ? $"💸 <b>Projected Earnings:</b>"
            : $"💸 <b>Anumaanit Aamdani:</b>"));

        profileText.AppendLine(lang == "English"
            ? $"   • <i>Per Hour:</i> {earnedPerHour:N4} USDT"
            : $"   • <i>Har Ghante:</i> {earnedPerHour:N4} USDT");

        profileText.AppendLine(lang == "English"
            ? $"   • <i>Per Day:</i> {earnedPerDay:N4} USDT"
            : $"   • <i>Har Din:</i> {earnedPerDay:N4} USDT");

        profileText.AppendLine(lang == "English"
            ? $"   • <i>Per Week:</i> {earnedPerWeek:N4} USDT"
            : $"   • <i>Har Hafta:</i> {earnedPerWeek:N4} USDT");

        // Реферальна статистика
        profileText.AppendLine("\n" + (lang == "English"
            ? $"👥 <b>Referral Stats:</b>"
            : $"👥 <b>Referral Ke Ankde:</b>"));

        profileText.AppendLine(lang == "English"
            ? $"   • <i>Total Referrals:</i> {referralsCount}"
            : $"   • <i>Total Referrals:</i> {referralsCount}");

        // Очікуваний прибуток від рефералів (ПРАВИЛЬНО)
        profileText.AppendLine(lang == "English"
            ? $"   • <i>Referrals' active investments profit:</i> {totalReferralProfit:N2} USDT"
            : $"   • <i>Referrals ke active investments ka profit:</i> {totalReferralProfit:N2} USDT");

        decimal userPercentage = _referralService.GetUserReferralPercentage(user.Id);
        int displayPercentage = (int)(userPercentage * 100);

        profileText.AppendLine(lang == "English"
            ? $"   • <i>Your expected profit ({displayPercentage}%):</i> {expectedReferralReward:N2} USDT"
            : $"   • <i>Aapka expected profit ({displayPercentage}%):</i> {expectedReferralReward:N2} USDT");

        // Активні інвестиції
        if (activeInvestments.Any())
        {
            profileText.AppendLine("\n" + (lang == "English"
                ? $"📈 <b>Active Investments:</b>"
                : $"📈 <b>Aktive Investments:</b>"));

            decimal totalCurrentProfit = 0;

            foreach (var inv in activeInvestments)
            {
                var timeLeft = inv.StartDate.AddHours(inv.DurationHours) - DateTime.UtcNow;
                var progress = (DateTime.UtcNow - inv.StartDate).TotalHours / inv.DurationHours * 100;
                var days = inv.DurationHours / 24;

                var elapsedHours = (DateTime.UtcNow - inv.StartDate).TotalHours;
                var currentProfit = inv.Amount * (decimal)(inv.InterestPercent / 100) *
                                    (decimal)(elapsedHours / inv.DurationHours);
                totalCurrentProfit += currentProfit;

                profileText.AppendLine(lang == "English"
                    ? $"   • <b>{inv.Amount:N2} USDT</b> for {days} days"
                    : $"   • <b>{inv.Amount:N2} USDT</b> {days} din ke liye");

                profileText.AppendLine(lang == "English"
                    ? $"     📊 <i>Progress:</i> {progress:F1}% | ⏳ <i>Time left:</i> {timeLeft:d\\.hh\\:mm}"
                    : $"     📊 <i>Progress:</i> {progress:F1}% | ⏳ <i>Bacha hua time:</i> {timeLeft:d\\.hh\\:mm}");

                profileText.AppendLine(lang == "English"
                    ? $"     💰 <i>Estimated profit:</i> ∼{currentProfit:F2} USDT | 📈 <i>Rate:</i> {inv.InterestPercent}%"
                    : $"     💰 <i>Anumaanit faayda:</i> ∼{currentProfit:F2} USDT | 📈 <i>Dar:</i> {inv.InterestPercent}%");
            }

            profileText.AppendLine("\n" + (lang == "English"
                ? $"💰 <b>Total Estimated Profit:</b> ∼{totalCurrentProfit:F2} USDT"
                : $"💰 <b>Kul Anumaanit Faayda:</b> ∼{totalCurrentProfit:F2} USDT"));
        }

        // ✅ Завершені інвестиції
        var completedInvestments = await _dbContext.Investments
            .Where(i => i.UserId == user.Id && !i.IsActive && i.ProfitAddedToBalance)
            .OrderByDescending(i => i.EndDate)
            .Take(3)
            .ToListAsync();

        if (completedInvestments.Any())
        {
            profileText.AppendLine("\n" + (lang == "English"
                ? $"✅ <b>Completed Investments:</b>"
                : $"✅ <b>Poori Hui Investments:</b>"));

            foreach (var inv in completedInvestments)
            {
                decimal totalReturn = inv.Amount + inv.AccumulatedProfit;
                profileText.AppendLine(lang == "English"
                    ? $"   • <b>{inv.Amount:N2} USDT</b> for {inv.DurationHours / 24} days"
                    : $"   • <b>{inv.Amount:N2} USDT</b> {inv.DurationHours / 24} din ke liye");
                profileText.AppendLine(lang == "English"
                    ? $"     <i>Profit:</i> +{inv.AccumulatedProfit:N2} USDT | <i>Total:</i> {totalReturn:N2} USDT"
                    : $"     <i>Profit:</i> +{inv.AccumulatedProfit:N2} USDT | <i>Kul:</i> {totalReturn:N2} USDT");
                profileText.AppendLine(lang == "English"
                    ? $"     <i>Rate:</i> {inv.InterestPercent}% | <i>Completed on:</i> {inv.EndDate:dd.MM.yyyy}"
                    : $"     <i>Dar:</i> {inv.InterestPercent}% | <i>Complete hui:</i> {inv.EndDate:dd.MM.yyyy}");
            }
        }
        // Додаткова інформація
        profileText.AppendLine("\n" + (lang == "English"
            ? $"📅 <b>Registration Date:</b> {user.RegistrationDate:dd.MM.yyyy}"
            : $"📅 <b>Registration Date:</b> {user.RegistrationDate:dd.MM.yyyy}"));

        profileText.AppendLine(lang == "English"
            ? $"🟢 <b>Status:</b> {(user.IsAuthorized ? "Active" : "Inactive")}"
            : $"🟢 <b>Status:</b> {(user.IsAuthorized ? "Aktive" : "Naktive")}");

        // Кнопки меню
        var menuButtons = new InlineKeyboardMarkup(new[]
        {
            new[]
            {
                InlineKeyboardButton.WithCallbackData(lang == "English" ? "💳 Deposit" : "💳 Jama Karein", "deposit"),
                InlineKeyboardButton.WithCallbackData(lang == "English" ? "🏠 Withdraw" : "🏠 Paisa Nikalo", "withdraw")
            },
            new[]
            {
                InlineKeyboardButton.WithCallbackData(lang == "English" ? "🔙 Back to Menu" : "🔙 Wapas Menu", "back_to_menu")
            }
        });

        if (messageId.HasValue)
        {
            try
            {
                await _botClient.EditMessageText(
                    chatId: chatId,
                    messageId: messageId.Value,
                    text: profileText.ToString(),
                    parseMode: ParseMode.Html,
                    replyMarkup: menuButtons
                );
                return;
            }
            catch { }
        }
        await _botClient.SendMessage(
            chatId: chatId,
            text: profileText.ToString(),
            parseMode: ParseMode.Html,
            replyMarkup: menuButtons
        );
    }

    public async Task<decimal> GetBalanceAsync(long chatId)
    {
        // Отримуємо користувача з бази даних за chatId
        var user = await _dbContext.Users
            .FirstOrDefaultAsync(u => u.TelegramId == chatId);

        if (user == null)
        {
            // Якщо користувача не знайдено, можна повернути 0 або викинути виключення
            return 0m;
            // Або: throw new Exception("User not found");
        }

        return user.Balance;
    }

    public async Task<decimal> GetTotalInvestedAsync(long chatId)
    {
        var user = await _manageService.GetUserByTelegramIdAsync(chatId);
        if (user == null)
            return 0;

        // Враховуємо ВСІ інвестиції (як активні, так і завершені)
        var totalInvested = await _dbContext.Investments
            .Where(i => i.UserId == user.Id)
            .SumAsync(i => (decimal?)i.Amount) ?? 0m;

        return totalInvested;
    }

    private async Task<decimal> GetHourlyProfitAsync(long chatId)
    {
        var user = await _dbContext.Users
            .Include(u => u.Investments)
            .FirstOrDefaultAsync(u => u.TelegramId == chatId)
            .ConfigureAwait(false);

        if (user == null) return 0;

        var activeInvestments = user.Investments
            .Where(i => i.IsActive && i.DurationHours > 0 && DateTime.UtcNow < i.StartDate.AddHours(i.DurationHours))
            .ToList();

        if (!activeInvestments.Any())
            return 0;

        decimal totalHourlyProfit = 0;

        foreach (var inv in activeInvestments)
        {
            // Скільки часу залишилось до завершення інвестиції
            var remainingHours = (inv.StartDate.AddHours(inv.DurationHours) - DateTime.UtcNow).TotalHours;
            remainingHours = Math.Max(0, remainingHours);

            // Прибуток за годину з урахуванням залишкового часу
            decimal hourlyProfit = (inv.Amount * (inv.InterestPercent / 100m)) / inv.DurationHours;
            totalHourlyProfit += hourlyProfit;
        }

        return totalHourlyProfit;
    }

    private async Task<decimal> GetAccumulatedProfitAsync(long chatId)
    {
        var user = await _manageService.GetUserByTelegramIdAsync(chatId);
        if (user == null) return 0;

        // Враховуємо ВСІ прибутки (як додані до балансу, так і ні)
        return await _dbContext.Investments
            .Where(i => i.UserId == user.Id && !i.IsActive)
            .SumAsync(i => (decimal?)i.AccumulatedProfit) ?? 0m;
    }

    private int GetInvestmentTotalHours(InvestmentDuration duration)
    {
        return duration switch
        {
            InvestmentDuration.TwoDays => 48,
            InvestmentDuration.OneWeek => 168,
            InvestmentDuration.TwoWeeks => 336,
            _ => 0
        };
    }

    private async Task CheckAndNotifyCompletedInvestments(long chatId,  bool skipProfileUpdate = false)
    {
        var user = await _dbContext.Users
            .Include(u => u.Investments)
            .FirstOrDefaultAsync(u => u.TelegramId == chatId);

        if (user == null) return;

        bool changesMade = false;

        foreach (var investment in user.Investments.Where(i => i.IsActive && !i.ProfitAddedToBalance))
        {
            var timePassed = DateTime.UtcNow - investment.StartDate;
            var remainingHours = investment.DurationHours - (decimal)timePassed.TotalHours;

            if (remainingHours <= 0)
            {
                changesMade = true;

                // Розрахунок прибуткуж
                decimal profit = investment.Amount * (investment.InterestPercent / 100m);
                decimal totalAmount = investment.Amount + profit;

                // Оновлюємо інвестицію 
                investment.IsActive = false;
                investment.EndDate = DateTime.UtcNow;
                investment.AccumulatedProfit = profit;
                investment.ProfitAddedToBalance = true;
                user.Balance += totalAmount;

                // Відправляємо сповіщення з кнопкою "Назад"
                var menuMarkup = new InlineKeyboardMarkup(new[]
                {
                    new[]
                    {
                        InlineKeyboardButton.WithCallbackData(
                            user.Language == "English" ? "📊 View Profile" : "📊 Profile Dekhein",
                            "my_profile"),
                        InlineKeyboardButton.WithCallbackData(
                            user.Language == "English" ? "💼 Main Menu" : "💼 Main Menu",
                            "back_to_menu")
                    }
                });

                await _botClient.SendMessage(
                    chatId,
                    user.Language == "English"
                        ? $"🎉 INVESTMENT COMPLETED! 🎉\n\n" +
                          $"💰 Amount: {investment.Amount:N2} USDT\n" +
                          $"📈 Profit: +{profit:N2} USDT\n" +
                          $"💎 Total: {totalAmount:N2} USDT\n\n" +
                          $"✅ Added to your balance!\n" +
                          $"🚀 New Balance: {user.Balance:N2} USDT"
                        : $"🎉 Investment Complete! 🎉\n\n" +
                          $"💰 Rashi: {investment.Amount:N2} USDT\n" +
                          $"📈 Profit: +{profit:N2} USDT\n" +
                          $"💎 Total: {totalAmount:N2} USDT\n\n" +
                          $"✅ Balance mein add ho gaya!\n" +
                          $"🚀 Naya Balance: {user.Balance:N2} USDT",
                    replyMarkup: menuMarkup);

                // Реферальна винагорода
                if (user.ReferredBy.HasValue)
                {
                    var referrer = await _dbContext.Users
                        .FirstOrDefaultAsync(u => u.Id == user.ReferredBy.Value);

                    if (referrer != null)
                    {
                        decimal rewardPercentage = _referralService.GetUserReferralPercentage(referrer.Id);
                        decimal rewardAmount = profit * rewardPercentage;
                        int rewardPercent = (int)(rewardPercentage * 100);

                        referrer.Balance += rewardAmount;
                        referrer.TotalReferralRewards += rewardAmount;

                        await _botClient.SendMessage(referrer.TelegramId,
                            referrer.Language == "English"
                                ? $"🎁 REFERRAL REWARD! 🎁\n\n" +
                                  $"👥 From your referral\n" +
                                  $"💰 Profit: {profit:N2}USDT\n" +
                                  $"🎯 Your reward ({rewardPercent}%): {rewardAmount:N2}USDT\n\n" +
                                  $"✅ Added to your balance!"
                                : $"🎁 Referral Reward! 🎁\n\n" +
                                  $"👥 Aapke referral se\n" +
                                  $"💰 Profit: {profit:N2}USDT\n" +
                                  $"🎯 Aapka reward ({rewardPercent}%): {rewardAmount:N2}USDT\n\n" +
                                  $"✅ Balance mein add ho gaya!",
                            replyMarkup: menuMarkup);
                    }
                }
            }
        }

        if (changesMade)
        {
            await _dbContext.SaveChangesAsync();
        }
    }
    
    public async Task CheckInvestmentsForUser(long chatId)
    {
        await CheckAndNotifyCompletedInvestments(chatId, true);
    }
    
}