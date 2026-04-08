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
    private readonly ILocalizationService _localizationService;

    public ProfileService(ITelegramBotClient botClient, IManageService manageService, BotDbContext dbContext,
        IUserStateService userStateService, IReferralService referralService, ITopUserService topUserService,
        ILocalizationService localizationService,
        Func<long, Task>? showMainMenuAsync = null)
    {
        _botClient = botClient;
        _manageService = manageService;
        _dbContext = dbContext;
        _userStateService = userStateService;
        _referralService = referralService;
        _topUserService = topUserService;
        _localizationService = localizationService;
        _showMainMenuAsync = showMainMenuAsync;
    }

    private string T(string? language, string english, string hinglish, string russian, string farsi, string arabic, string chinese)
    {
        var normalized = _localizationService.NormalizeCode(language);
        var text = normalized switch
        {
            BotLanguageCodes.Hinglish => hinglish,
            BotLanguageCodes.Russian => russian,
            BotLanguageCodes.Farsi => farsi,
            BotLanguageCodes.Arabic => arabic,
            BotLanguageCodes.SimplifiedChinese => chinese,
            _ => english
        };

        return _localizationService.FormatText(normalized, text);
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
            string notFoundMsg = T(user?.PreferredLanguage ?? user?.Language,
                "❗️Profile not found. Try again later or register.",
                "❗️Profile nahi mila. Phir se koshish karein ya register karein.",
                "❗️Профиль не найден. Попробуйте позже или зарегистрируйтесь.",
                "❗️پروفایل پیدا نشد. بعداً دوباره تلاش کنید یا ثبت نام کنید.",
                "❗️لم يتم العثور على الملف الشخصي. حاول لاحقاً أو قم بالتسجيل.",
                "❗️未找到个人资料。请稍后再试或完成注册。");
            await _botClient.SendMessage(chatId, notFoundMsg);
            return;
        }

        var lang = user.PreferredLanguage ?? user.Language ?? BotLanguageCodes.English;

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
        profileText.AppendLine(T(lang, "🙋‍♂️ <b>My Profile</b>\n", "🙋‍♂️ <b>Mera Profile</b>\n", "🙋‍♂️ <b>Мой профиль</b>\n", "🙋‍♂️ <b>پروفایل من</b>\n", "🙋‍♂️ <b>ملفي الشخصي</b>\n", "🙋‍♂️ <b>我的资料</b>\n"));

        // Основна інформація про баланси
        profileText.AppendLine(T(lang, $"💰 <b>Current Balance:</b> {user.Balance:N2} USDT", $"💰 <b>Maujooda Balance:</b> {user.Balance:N2} USDT", $"💰 <b>Текущий баланс:</b> {user.Balance:N2} USDT", $"💰 <b>موجودی فعلی:</b> {user.Balance:N2} USDT", $"💰 <b>الرصيد الحالي:</b> {user.Balance:N2} USDT", $"💰 <b>当前余额：</b>{user.Balance:N2} USDT"));

        profileText.AppendLine(T(lang, $"💼 <b>Total invested:</b> {totalInvested:N2} USDT", $"💼 <b>Kul invest kiya:</b> {totalInvested:N2} USDT", $"💼 <b>Всего инвестировано:</b> {totalInvested:N2} USDT", $"💼 <b>کل سرمایه گذاری:</b> {totalInvested:N2} USDT", $"💼 <b>إجمالي الاستثمار:</b> {totalInvested:N2} USDT", $"💼 <b>累计投资：</b>{totalInvested:N2} USDT"));

        profileText.AppendLine(T(lang, $"📈 <b>Accumulated Profit:</b> {accumulatedProfit:N2} USDT", $"📈 <b>Jama Munafa:</b> {accumulatedProfit:N2} USDT", $"📈 <b>Накопленная прибыль:</b> {accumulatedProfit:N2} USDT", $"📈 <b>سود انباشته:</b> {accumulatedProfit:N2} USDT", $"📈 <b>الأرباح المتراكمة:</b> {accumulatedProfit:N2} USDT", $"📈 <b>累计利润：</b>{accumulatedProfit:N2} USDT"));

        profileText.AppendLine(T(lang, $"🎯 <b>Referral Rewards:</b> {totalReferralRewards:N2} USDT", $"🎯 <b>Referral Rewards:</b> {totalReferralRewards:N2} USDT", $"🎯 <b>Реферальные награды:</b> {totalReferralRewards:N2} USDT", $"🎯 <b>پاداش های دعوت:</b> {totalReferralRewards:N2} USDT", $"🎯 <b>مكافآت الإحالة:</b> {totalReferralRewards:N2} USDT", $"🎯 <b>邀请奖励：</b>{totalReferralRewards:N2} USDT"));

        // Прогнозований дохід
        profileText.AppendLine("\n" + T(lang, "💸 <b>Projected Earnings:</b>", "💸 <b>Anumaanit Aamdani:</b>", "💸 <b>Прогнозируемый доход:</b>", "💸 <b>درآمد پیش بینی شده:</b>", "💸 <b>الأرباح المتوقعة:</b>", "💸 <b>预计收益：</b>"));

        profileText.AppendLine(T(lang, $"   • <i>Per Hour:</i> {earnedPerHour:N4} USDT", $"   • <i>Har Ghante:</i> {earnedPerHour:N4} USDT", $"   • <i>В час:</i> {earnedPerHour:N4} USDT", $"   • <i>در هر ساعت:</i> {earnedPerHour:N4} USDT", $"   • <i>في الساعة:</i> {earnedPerHour:N4} USDT", $"   • <i>每小时：</i>{earnedPerHour:N4} USDT"));

        profileText.AppendLine(T(lang, $"   • <i>Per Day:</i> {earnedPerDay:N4} USDT", $"   • <i>Har Din:</i> {earnedPerDay:N4} USDT", $"   • <i>В день:</i> {earnedPerDay:N4} USDT", $"   • <i>در روز:</i> {earnedPerDay:N4} USDT", $"   • <i>في اليوم:</i> {earnedPerDay:N4} USDT", $"   • <i>每天：</i>{earnedPerDay:N4} USDT"));

        profileText.AppendLine(T(lang, $"   • <i>Per Week:</i> {earnedPerWeek:N4} USDT", $"   • <i>Har Hafta:</i> {earnedPerWeek:N4} USDT", $"   • <i>В неделю:</i> {earnedPerWeek:N4} USDT", $"   • <i>در هفته:</i> {earnedPerWeek:N4} USDT", $"   • <i>في الأسبوع:</i> {earnedPerWeek:N4} USDT", $"   • <i>每周：</i>{earnedPerWeek:N4} USDT"));

        // Реферальна статистика
        profileText.AppendLine("\n" + T(lang, "👥 <b>Referral Stats:</b>", "👥 <b>Referral Ke Ankde:</b>", "👥 <b>Статистика рефералов:</b>", "👥 <b>آمار دعوت:</b>", "👥 <b>إحصاءات الإحالة:</b>", "👥 <b>邀请统计：</b>"));

        profileText.AppendLine(T(lang, $"   • <i>Total Referrals:</i> {referralsCount}", $"   • <i>Kul Referrals:</i> {referralsCount}", $"   • <i>Всего рефералов:</i> {referralsCount}", $"   • <i>مجموع دعوت ها:</i> {referralsCount}", $"   • <i>إجمالي الإحالات:</i> {referralsCount}", $"   • <i>邀请总数：</i>{referralsCount}"));

        // Очікуваний прибуток від рефералів (ПРАВИЛЬНО)
        profileText.AppendLine(T(lang, $"   • <i>Referrals' active investments profit:</i> {totalReferralProfit:N2} USDT", $"   • <i>Referrals ke active investments ka profit:</i> {totalReferralProfit:N2} USDT", $"   • <i>Прибыль от активных инвестиций рефералов:</i> {totalReferralProfit:N2} USDT", $"   • <i>سود سرمایه گذاری فعال دعوت شدگان:</i> {totalReferralProfit:N2} USDT", $"   • <i>أرباح الاستثمارات النشطة للإحالات:</i> {totalReferralProfit:N2} USDT", $"   • <i>邀请用户活跃投资利润：</i>{totalReferralProfit:N2} USDT"));

        decimal userPercentage = _referralService.GetUserReferralPercentage(user.Id);
        int displayPercentage = (int)(userPercentage * 100);

        profileText.AppendLine(T(lang, $"   • <i>Your expected profit ({displayPercentage}%):</i> {expectedReferralReward:N2} USDT", $"   • <i>Aapka expected profit ({displayPercentage}%):</i> {expectedReferralReward:N2} USDT", $"   • <i>Ваш ожидаемый доход ({displayPercentage}%):</i> {expectedReferralReward:N2} USDT", $"   • <i>سود مورد انتظار شما ({displayPercentage}%):</i> {expectedReferralReward:N2} USDT", $"   • <i>ربحك المتوقع ({displayPercentage}%):</i> {expectedReferralReward:N2} USDT", $"   • <i>您的预期收益（{displayPercentage}%）：</i>{expectedReferralReward:N2} USDT"));

        // Активні інвестиції
        if (activeInvestments.Any())
        {
            profileText.AppendLine("\n" + T(lang, "📈 <b>Active Investments:</b>", "📈 <b>Active Investments:</b>", "📈 <b>Активные инвестиции:</b>", "📈 <b>سرمایه گذاری های فعال:</b>", "📈 <b>الاستثمارات النشطة:</b>", "📈 <b>活跃投资：</b>"));

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

                profileText.AppendLine(T(lang, $"   • <b>{inv.Amount:N2} USDT</b> for {days} days", $"   • <b>{inv.Amount:N2} USDT</b> {days} din ke liye", $"   • <b>{inv.Amount:N2} USDT</b> на {days} дней", $"   • <b>{inv.Amount:N2} USDT</b> برای {days} روز", $"   • <b>{inv.Amount:N2} USDT</b> لمدة {days} أيام", $"   • <b>{inv.Amount:N2} USDT</b>，周期 {days} 天"));

                profileText.AppendLine(T(lang, $"     📊 <i>Progress:</i> {progress:F1}% | ⏳ <i>Time left:</i> {timeLeft:d\\.hh\\:mm}", $"     📊 <i>Progress:</i> {progress:F1}% | ⏳ <i>Bacha hua time:</i> {timeLeft:d\\.hh\\:mm}", $"     📊 <i>Прогресс:</i> {progress:F1}% | ⏳ <i>Осталось времени:</i> {timeLeft:d\\.hh\\:mm}", $"     📊 <i>پیشرفت:</i> {progress:F1}% | ⏳ <i>زمان باقی مانده:</i> {timeLeft:d\\.hh\\:mm}", $"     📊 <i>التقدم:</i> {progress:F1}% | ⏳ <i>الوقت المتبقي:</i> {timeLeft:d\\.hh\\:mm}", $"     📊 <i>进度：</i>{progress:F1}% | ⏳ <i>剩余时间：</i>{timeLeft:d\\.hh\\:mm}"));

                profileText.AppendLine(T(lang, $"     💰 <i>Estimated profit:</i> ∼{currentProfit:F2} USDT | 📈 <i>Rate:</i> {inv.InterestPercent}%", $"     💰 <i>Anumaanit faayda:</i> ∼{currentProfit:F2} USDT | 📈 <i>Dar:</i> {inv.InterestPercent}%", $"     💰 <i>Ожидаемая прибыль:</i> ∼{currentProfit:F2} USDT | 📈 <i>Ставка:</i> {inv.InterestPercent}%", $"     💰 <i>سود تخمینی:</i> ∼{currentProfit:F2} USDT | 📈 <i>نرخ:</i> {inv.InterestPercent}%", $"     💰 <i>الربح المتوقع:</i> ∼{currentProfit:F2} USDT | 📈 <i>المعدل:</i> {inv.InterestPercent}%", $"     💰 <i>预估利润：</i>∼{currentProfit:F2} USDT | 📈 <i>利率：</i>{inv.InterestPercent}%"));
            }

            profileText.AppendLine("\n" + T(lang, $"💰 <b>Total Estimated Profit:</b> ∼{totalCurrentProfit:F2} USDT", $"💰 <b>Kul Anumaanit Faayda:</b> ∼{totalCurrentProfit:F2} USDT", $"💰 <b>Общая ожидаемая прибыль:</b> ∼{totalCurrentProfit:F2} USDT", $"💰 <b>کل سود تخمینی:</b> ∼{totalCurrentProfit:F2} USDT", $"💰 <b>إجمالي الربح المتوقع:</b> ∼{totalCurrentProfit:F2} USDT", $"💰 <b>预计总利润：</b>∼{totalCurrentProfit:F2} USDT"));
        }

        // ✅ Завершені інвестиції
        var completedInvestments = await _dbContext.Investments
            .Where(i => i.UserId == user.Id && !i.IsActive && i.ProfitAddedToBalance)
            .OrderByDescending(i => i.EndDate)
            .Take(3)
            .ToListAsync();

        if (completedInvestments.Any())
        {
            profileText.AppendLine("\n" + T(lang, "✅ <b>Completed Investments:</b>", "✅ <b>Poori Hui Investments:</b>", "✅ <b>Завершенные инвестиции:</b>", "✅ <b>سرمایه گذاری های تکمیل شده:</b>", "✅ <b>الاستثمارات المكتملة:</b>", "✅ <b>已完成投资：</b>"));

            foreach (var inv in completedInvestments)
            {
                decimal totalReturn = inv.Amount + inv.AccumulatedProfit;
                profileText.AppendLine(T(lang, $"   • <b>{inv.Amount:N2} USDT</b> for {inv.DurationHours / 24} days", $"   • <b>{inv.Amount:N2} USDT</b> {inv.DurationHours / 24} din ke liye", $"   • <b>{inv.Amount:N2} USDT</b> на {inv.DurationHours / 24} дней", $"   • <b>{inv.Amount:N2} USDT</b> برای {inv.DurationHours / 24} روز", $"   • <b>{inv.Amount:N2} USDT</b> لمدة {inv.DurationHours / 24} أيام", $"   • <b>{inv.Amount:N2} USDT</b>，周期 {inv.DurationHours / 24} 天"));
                profileText.AppendLine(T(lang, $"     <i>Profit:</i> +{inv.AccumulatedProfit:N2} USDT | <i>Total:</i> {totalReturn:N2} USDT", $"     <i>Profit:</i> +{inv.AccumulatedProfit:N2} USDT | <i>Kul:</i> {totalReturn:N2} USDT", $"     <i>Прибыль:</i> +{inv.AccumulatedProfit:N2} USDT | <i>Итого:</i> {totalReturn:N2} USDT", $"     <i>سود:</i> +{inv.AccumulatedProfit:N2} USDT | <i>جمع:</i> {totalReturn:N2} USDT", $"     <i>الربح:</i> +{inv.AccumulatedProfit:N2} USDT | <i>الإجمالي:</i> {totalReturn:N2} USDT", $"     <i>利润：</i>+{inv.AccumulatedProfit:N2} USDT | <i>总计：</i>{totalReturn:N2} USDT"));
                profileText.AppendLine(T(lang, $"     <i>Rate:</i> {inv.InterestPercent}% | <i>Completed on:</i> {inv.EndDate:dd.MM.yyyy}", $"     <i>Dar:</i> {inv.InterestPercent}% | <i>Complete hui:</i> {inv.EndDate:dd.MM.yyyy}", $"     <i>Ставка:</i> {inv.InterestPercent}% | <i>Завершено:</i> {inv.EndDate:dd.MM.yyyy}", $"     <i>نرخ:</i> {inv.InterestPercent}% | <i>تکمیل شده در:</i> {inv.EndDate:dd.MM.yyyy}", $"     <i>المعدل:</i> {inv.InterestPercent}% | <i>اكتمل في:</i> {inv.EndDate:dd.MM.yyyy}", $"     <i>利率：</i>{inv.InterestPercent}% | <i>完成于：</i>{inv.EndDate:dd.MM.yyyy}"));
            }
        }
        // Додаткова інформація
        profileText.AppendLine("\n" + T(lang, $"📅 <b>Registration Date:</b> {user.RegistrationDate:dd.MM.yyyy}", $"📅 <b>Registration Date:</b> {user.RegistrationDate:dd.MM.yyyy}", $"📅 <b>Дата регистрации:</b> {user.RegistrationDate:dd.MM.yyyy}", $"📅 <b>تاریخ ثبت نام:</b> {user.RegistrationDate:dd.MM.yyyy}", $"📅 <b>تاريخ التسجيل:</b> {user.RegistrationDate:dd.MM.yyyy}", $"📅 <b>注册日期：</b>{user.RegistrationDate:dd.MM.yyyy}"));

        profileText.AppendLine(T(lang, $"🟢 <b>Status:</b> {(user.IsAuthorized ? "Active" : "Inactive")}", $"🟢 <b>Status:</b> {(user.IsAuthorized ? "Active" : "Inactive")}", $"🟢 <b>Статус:</b> {(user.IsAuthorized ? "Активен" : "Неактивен")}", $"🟢 <b>وضعیت:</b> {(user.IsAuthorized ? "فعال" : "غیرفعال")}", $"🟢 <b>الحالة:</b> {(user.IsAuthorized ? "نشط" : "غير نشط")}", $"🟢 <b>状态：</b>{(user.IsAuthorized ? "活跃" : "未激活")}"));

        // Кнопки меню
        var menuButtons = new InlineKeyboardMarkup(new[]
        {
            new[]
            {
                InlineKeyboardButton.WithCallbackData(T(lang, "💳 Deposit", "💳 Jama Karein", "💳 Пополнить", "💳 واریز", "💳 إيداع", "💳 充值"), "deposit"),
                InlineKeyboardButton.WithCallbackData(T(lang, "🏠 Withdraw", "🏠 Paisa Nikalo", "🏠 Вывести", "🏠 برداشت", "🏠 سحب", "🏠 提现"), "withdraw")
            },
            new[]
            {
                InlineKeyboardButton.WithCallbackData(T(lang, "🔙 Back to Menu", "🔙 Wapas Menu", "🔙 Назад в меню", "🔙 بازگشت به منو", "🔙 العودة إلى القائمة", "🔙 返回菜单"), "back_to_menu")
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
                            T(user.PreferredLanguage ?? user.Language, "📊 View Profile", "📊 Profile Dekhein", "📊 Открыть профиль", "📊 مشاهده پروفایل", "📊 عرض الملف الشخصي", "📊 查看资料"),
                            "my_profile"),
                        InlineKeyboardButton.WithCallbackData(
                            T(user.PreferredLanguage ?? user.Language, "💼 Main Menu", "💼 Main Menu", "💼 Главное меню", "💼 منوی اصلی", "💼 القائمة الرئيسية", "💼 主菜单"),
                            "back_to_menu")
                    }
                });

                await _botClient.SendMessage(
                    chatId,
                    T(user.PreferredLanguage ?? user.Language,
                        $"🎉 INVESTMENT COMPLETED! 🎉\n\n💰 Amount: {investment.Amount:N2} USDT\n📈 Profit: +{profit:N2} USDT\n💎 Total: {totalAmount:N2} USDT\n\n✅ Added to your balance!\n🚀 New Balance: {user.Balance:N2} USDT",
                        $"🎉 Investment Complete! 🎉\n\n💰 Rashi: {investment.Amount:N2} USDT\n📈 Profit: +{profit:N2} USDT\n💎 Total: {totalAmount:N2} USDT\n\n✅ Balance mein add ho gaya!\n🚀 Naya Balance: {user.Balance:N2} USDT",
                        $"🎉 ИНВЕСТИЦИЯ ЗАВЕРШЕНА! 🎉\n\n💰 Сумма: {investment.Amount:N2} USDT\n📈 Прибыль: +{profit:N2} USDT\n💎 Итого: {totalAmount:N2} USDT\n\n✅ Добавлено на ваш баланс!\n🚀 Новый баланс: {user.Balance:N2} USDT",
                        $"🎉 سرمایه گذاری تکمیل شد! 🎉\n\n💰 مبلغ: {investment.Amount:N2} USDT\n📈 سود: +{profit:N2} USDT\n💎 مجموع: {totalAmount:N2} USDT\n\n✅ به موجودی شما اضافه شد!\n🚀 موجودی جدید: {user.Balance:N2} USDT",
                        $"🎉 اكتمل الاستثمار! 🎉\n\n💰 المبلغ: {investment.Amount:N2} USDT\n📈 الربح: +{profit:N2} USDT\n💎 الإجمالي: {totalAmount:N2} USDT\n\n✅ تمت إضافته إلى رصيدك!\n🚀 الرصيد الجديد: {user.Balance:N2} USDT",
                        $"🎉 投资已完成！🎉\n\n💰 金额：{investment.Amount:N2} USDT\n📈 利润：+{profit:N2} USDT\n💎 总计：{totalAmount:N2} USDT\n\n✅ 已添加到您的余额！\n🚀 新余额：{user.Balance:N2} USDT"),
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
                                                        T(referrer.PreferredLanguage ?? referrer.Language,
                                                                $"🎁 REFERRAL REWARD! 🎁\n\n👥 From your referral\n💰 Profit: {profit:N2}USDT\n🎯 Your reward ({rewardPercent}%): {rewardAmount:N2}USDT\n\n✅ Added to your balance!",
                                                                $"🎁 Referral Reward! 🎁\n\n👥 Aapke referral se\n💰 Profit: {profit:N2}USDT\n🎯 Aapka reward ({rewardPercent}%): {rewardAmount:N2}USDT\n\n✅ Balance mein add ho gaya!",
                                                                $"🎁 РЕФЕРАЛЬНАЯ НАГРАДА! 🎁\n\n👥 От вашего реферала\n💰 Прибыль: {profit:N2}USDT\n🎯 Ваша награда ({rewardPercent}%): {rewardAmount:N2}USDT\n\n✅ Добавлено на ваш баланс!",
                                                                $"🎁 پاداش دعوت! 🎁\n\n👥 از طرف کاربر دعوت شده شما\n💰 سود: {profit:N2}USDT\n🎯 پاداش شما ({rewardPercent}%): {rewardAmount:N2}USDT\n\n✅ به موجودی شما اضافه شد!",
                                                                $"🎁 مكافأة إحالة! 🎁\n\n👥 من إحالتك\n💰 الربح: {profit:N2}USDT\n🎯 مكافأتك ({rewardPercent}%): {rewardAmount:N2}USDT\n\n✅ تمت إضافتها إلى رصيدك!",
                                                                $"🎁 邀请奖励！🎁\n\n👥 来自您的邀请用户\n💰 利润：{profit:N2}USDT\n🎯 您的奖励（{rewardPercent}%）：{rewardAmount:N2}USDT\n\n✅ 已添加到您的余额！"),
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