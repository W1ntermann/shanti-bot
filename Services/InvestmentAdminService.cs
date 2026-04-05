using System.Text;
using Microsoft.EntityFrameworkCore;
using ShantiBotDi.Db;
using Telegram.Bot;
using Telegram.Bot.Types.Enums;

namespace ShantiBotDi.Services;

public class InvestmentAdminService : IInvestmentAdminService
{
    private readonly ITelegramBotClient _bot;
    private readonly BotDbContext _db;
    private readonly IManageService _manage;

    public InvestmentAdminService(ITelegramBotClient bot, BotDbContext db, IManageService manage)
    {
        _bot = bot;
        _db = db; 
        _manage = manage;
    }

    public async Task ShowAllUsersSummaryAsync(long adminChatId)
    {
        var lang = (await _manage.GetUserByTelegramIdAsync(adminChatId))?.Language ?? "English";

        var data = await _db.Users
            .Include(u => u.Investments)
            .Select(u => new
            {
                u.TelegramId,
                u.Username,
                u.Balance,
                ActiveInvestments = u.Investments.Where(i => i.IsActive).ToList(),
                AllInvestments = u.Investments.ToList()
            })
            .Where(u => u.ActiveInvestments.Any())
            .OrderBy(u => u.TelegramId)
            .ToListAsync();

        if (!data.Any())
        {
            await _bot.SendMessage(adminChatId,
                lang == "English" ? "❌ No active investments found." : "❌ Koi sakriya nivesh nahi mila.");
            return;
        }

        // Загальна статистика
        int totalUsers = data.Count;
        int totalActiveInvestments = data.Sum(u => u.ActiveInvestments.Count);
        decimal totalActiveAmount = data.Sum(u => u.ActiveInvestments.Sum(i => i.Amount));
        decimal totalActiveProfit = data.Sum(u => u.ActiveInvestments.Sum(i => i.AccumulatedProfit));
        decimal totalBalance = data.Sum(u => u.Balance);

        decimal totalExpectedProfit = 0;
        foreach (var user in data)
        {
            foreach (var investment in user.ActiveInvestments)
            {
                decimal expectedTotalProfit = investment.Amount * (investment.InterestPercent / 100m);
                decimal remainingProfit = expectedTotalProfit - investment.AccumulatedProfit;
                if (remainingProfit > 0)
                    totalExpectedProfit += remainingProfit;
            }
        }

        // Заголовок з загальною статистикою
        var header = new StringBuilder();
        if (lang == "English")
        {
            header.AppendLine($"📊 <b>All Users - Active Investments Summary</b>");
            header.AppendLine($"👥 Total Users: <b>{totalUsers}</b>");
            header.AppendLine($"💰 Total Balance: <b>{totalBalance:0.00} USDT</b>");
            header.AppendLine(
                $"🔥 Active Investments: <b>{totalActiveInvestments} | {totalActiveAmount:0.00} USDT</b>");
            header.AppendLine($"🎯 Accumulated Profit: <b>{totalActiveProfit:0.00} USDT</b>");
            header.AppendLine($"🚀 Expected Profit: <b>{totalExpectedProfit:0.00} USDT</b>");
        }
        else
        {
            header.AppendLine($"📊 <b>Sabhi Upyogakarta - Sakriya Nivesh Sankshipt</b>");
            header.AppendLine($"👥 Kul Upyogakarta: <b>{totalUsers}</b>");
            header.AppendLine($"💰 Kul Balance: <b>{totalBalance:0.00} USDT</b>");
            header.AppendLine($"🔥 Sakriya Nivesh: <b>{totalActiveInvestments} | {totalActiveAmount:0.00} USDT</b>");
            header.AppendLine($"🎯 Ikathitha Labh: <b>{totalActiveProfit:0.00} USDT</b>");
            header.AppendLine($"🚀 Apekshit Labh: <b>{totalExpectedProfit:0.00} USDT</b>");
        }

        await _bot.SendMessage(adminChatId, header.ToString(), parseMode: ParseMode.Html);

        // Детальний список користувачів та їх інвестицій
        foreach (var user in data)
        {
            string displayName = !string.IsNullOrEmpty(user.Username)
                ? $"@{user.Username}"
                : $"ID: {user.TelegramId}";

            decimal userActiveAmount = user.ActiveInvestments.Sum(i => i.Amount);
            decimal userActiveProfit = user.ActiveInvestments.Sum(i => i.AccumulatedProfit);

            decimal userExpectedProfit = 0;
            foreach (var investment in user.ActiveInvestments)
            {
                decimal expectedTotalProfit = investment.Amount * (investment.InterestPercent / 100m);
                decimal remainingProfit = expectedTotalProfit - investment.AccumulatedProfit;
                if (remainingProfit > 0)
                    userExpectedProfit += remainingProfit;
            }

            // Інформація про користувача
            var userInfo = new StringBuilder();
            if (lang == "English")
            {
                userInfo.AppendLine($"\n👤 <b>User: {displayName}</b>");
                userInfo.AppendLine($"🆔 <b>Telegram ID: {user.TelegramId}</b>");
                userInfo.AppendLine($"💰 Balance: <b>{user.Balance:0.00} USDT</b>");
                userInfo.AppendLine(
                    $"🔥 Active Investments: <b>{user.ActiveInvestments.Count} | {userActiveAmount:0.00} USDT</b>");
                userInfo.AppendLine($"🎯 Accumulated Profit: <b>{userActiveProfit:0.00} USDT</b>");
                userInfo.AppendLine($"🚀 Expected Profit: <b>{userExpectedProfit:0.00} USDT</b>");
            }
            else
            {
                userInfo.AppendLine($"\n👤 <b>Upyogakarta: {displayName}</b>");
                userInfo.AppendLine($"🆔 <b>Telegram ID: {user.TelegramId}</b>");
                userInfo.AppendLine($"💰 Balance: <b>{user.Balance:0.00} USDT</b>");
                userInfo.AppendLine(
                    $"🔥 Sakriya Nivesh: <b>{user.ActiveInvestments.Count} | {userActiveAmount:0.00} USDT</b>");
                userInfo.AppendLine($"🎯 Ikathitha Labh: <b>{userActiveProfit:0.00} USDT</b>");
                userInfo.AppendLine($"🚀 Apekshit Labh: <b>{userExpectedProfit:0.00} USDT</b>");
            }

            // Відправляємо інформацію про користувача
            await _bot.SendMessage(adminChatId, userInfo.ToString(), parseMode: ParseMode.Html);

            // Детальний список інвестицій користувача
            if (user.ActiveInvestments.Any())
            {
                foreach (var investment in user.ActiveInvestments.OrderByDescending(i => i.StartDate))
                {
                    DateTime endDate = investment.StartDate.AddHours(investment.DurationHours);
                    decimal expectedTotalProfit = investment.Amount * (investment.InterestPercent / 100m);
                    decimal expectedRemainingProfit = expectedTotalProfit - investment.AccumulatedProfit;

                    string investmentText = lang == "English"
                        ? $"📊 <b>Investment #{investment.Id}</b>\n" +
                          $"💵 Amount: <b>{investment.Amount:0.00} USDT</b>\n" +
                          $"📈 Interest: <b>{investment.InterestPercent:0.##}%</b>\n" +
                          $"🎯 Accumulated profit: <b>{investment.AccumulatedProfit:0.00} USDT</b>\n" +
                          $"🚀 Expected profit: <b>{expectedRemainingProfit:0.00} USDT</b>\n" +
                          $"⏰ Started: <b>{investment.StartDate:yyyy-MM-dd HH:mm}</b>\n" +
                          $"⏱️ Duration: <b>{investment.DurationHours} hours</b>\n" +
                          $"📅 Ends: <b>{endDate:yyyy-MM-dd HH:mm}</b>"
                        : $"📊 <b>Nivesh #{investment.Id}</b>\n" +
                          $"💵 Raashi: <b>{investment.Amount:0.00} USDT</b>\n" +
                          $"📈 Byaaj: <b>{investment.InterestPercent:0.##}%</b>\n" +
                          $"🎯 Ikathitha labh: <b>{investment.AccumulatedProfit:0.00} USDT</b>\n" +
                          $"🚀 Aakankshit labh: <b>{expectedRemainingProfit:0.00} USDT</b>\n" +
                          $"⏰ Shuru: <b>{investment.StartDate:yyyy-MM-dd HH:mm}</b>\n" +
                          $"⏱️ Avadhi: <b>{investment.DurationHours} ghante</b>\n" +
                          $"📅 Samapti: <b>{endDate:yyyy-MM-dd HH:mm}</b>";

                    await _bot.SendMessage(adminChatId, investmentText, parseMode: ParseMode.Html);
                }
            }
            else
            {
                await _bot.SendMessage(adminChatId,
                    lang == "English" ? "No active investments." : "Koi sakriya nivesh nahi.");
            }
        }
    }

    public async Task ShowUserInvestmentsAsync(long adminChatId, long targetTelegramId)
    {
        var lang = (await _manage.GetUserByTelegramIdAsync(adminChatId))?.Language ?? "English";

        var user = await _db.Users
            .Include(u => u.Investments)
            .FirstOrDefaultAsync(u => u.TelegramId == targetTelegramId);

        if (user == null)
        {
            await _bot.SendMessage(adminChatId,
                lang == "English" ? "❌ User not found." : "❌ Upyogakarta nahi mila.");
            return;
        }

        var activeInvestments = user.Investments.Where(i => i.IsActive).ToList();
        var completedInvestments = user.Investments.Where(i => !i.IsActive).ToList();

        decimal activeAmt = activeInvestments.Sum(i => i.Amount);
        decimal activeProfit = activeInvestments.Sum(i => i.AccumulatedProfit);
        decimal totalAmt = user.Investments.Sum(i => i.Amount);
        decimal totalProfit = user.Investments.Sum(i => i.AccumulatedProfit);

        // Отримуємо відображуване ім'я користувача
        string displayName = !string.IsNullOrEmpty(user.Username)
            ? $"@{user.Username}"
            : $"ID: {user.TelegramId}";

        // Заголовок з інформацією про користувача
        var header = new StringBuilder();
        if (lang == "English")
        {
            header.AppendLine($"👤 <b>User Investments: {displayName}</b>");
            header.AppendLine($"🆔 <b>Telegram ID: {user.TelegramId}</b>");
            header.AppendLine($"💰 Balance: <b>{user.Balance:0.00} USDT</b>");
            header.AppendLine($"🔥 Active: <b>{activeInvestments.Count} investments | {activeAmt:0.00} USDT</b>");
            header.AppendLine($"✅ Completed: <b>{completedInvestments.Count} investments</b>");
            header.AppendLine($"📊 Total invested: <b>{totalAmt:0.00} USDT</b>");
            header.AppendLine($"🏆 Total profit: <b>{totalProfit:0.00} USDT</b>");
        }
        else
        {
            header.AppendLine($"👤 <b>Upyogakarta Nivesh: {displayName}</b>");
            header.AppendLine($"🆔 <b>Telegram ID: {user.TelegramId}</b>");
            header.AppendLine($"💰 Balance: <b>{user.Balance:0.00} USDT</b>");
            header.AppendLine($"🔥 Sakriya: <b>{activeInvestments.Count} nivesh | {activeAmt:0.00} USDT</b>");
            header.AppendLine($"✅ Poorna: <b>{completedInvestments.Count} nivesh</b>");
            header.AppendLine($"📊 Kul niveshit: <b>{totalAmt:0.00} USDT</b>");
            header.AppendLine($"🏆 Kul labh: <b>{totalProfit:0.00} USDT</b>");
        }

        await _bot.SendMessage(adminChatId, header.ToString(), parseMode: ParseMode.Html);

        // Детальний список інвестицій
        if (user.Investments.Any())
        {
            var investments = user.Investments.OrderByDescending(i => i.StartDate).ToList();

            foreach (var investment in investments)
            {
                string status = investment.IsActive ? "🟢 Active" : "✅ Completed";
                string statusHindi = investment.IsActive ? "🟢 Sakriya" : "✅ Poorna";

                // Розрахунок очікуваного прибутку для активних інвестицій
                string expectedProfitText = "";
                string expectedProfitTextHindi = "";

                if (investment.IsActive)
                {
                    // Припускаємо, що очікуваний прибуток = загальний потенційний прибуток - вже накопичений
                    decimal expectedTotalProfit = investment.Amount * (investment.InterestPercent / 100);
                    decimal expectedRemainingProfit = expectedTotalProfit - investment.AccumulatedProfit;

                    expectedProfitText = $"\n📈 Expected profit: <b>{expectedRemainingProfit:0.00} USDT</b>";
                    expectedProfitTextHindi = $"\n📈 Aakankshit labh: <b>{expectedRemainingProfit:0.00} USDT</b>";
                }

                string investmentText = lang == "English"
                    ? $"\n📊 <b>Investment #{investment.Id}</b>\n" +
                      $"💵 Amount: <b>{investment.Amount:0.00} USDT</b>\n" +
                      $"📈 Interest: <b>{investment.InterestPercent:0.##}%</b>\n" +
                      $"🔄 Status: <b>{status}</b>\n" +
                      $"🎯 Accumulated profit: <b>{investment.AccumulatedProfit:0.00} USDT</b>" +
                      $"{expectedProfitText}\n" +
                      $"⏰ Started: <b>{investment.StartDate:yyyy-MM-dd HH:mm}</b>\n" +
                      $"⏱️ Duration: <b>{investment.DurationHours} hours</b>"
                    : $"\n📊 <b>Nivesh #{investment.Id}</b>\n" +
                      $"💵 Raashi: <b>{investment.Amount:0.00} USDT</b>\n" +
                      $"📈 Byaaj: <b>{investment.InterestPercent:0.##}%</b>\n" +
                      $"🔄 Stithi: <b>{statusHindi}</b>\n" +
                      $"🎯 Ikathitha labh: <b>{investment.AccumulatedProfit:0.00} USDT</b>" +
                      $"{expectedProfitTextHindi}\n" +
                      $"⏰ Shuru: <b>{investment.StartDate:yyyy-MM-dd HH:mm}</b>\n" +
                      $"⏱️ Avadhi: <b>{investment.DurationHours} ghante</b>";

                await _bot.SendMessage(adminChatId, investmentText, parseMode: ParseMode.Html);
            }
        }
        else
        {
            await _bot.SendMessage(adminChatId,
                lang == "English" ? "No investments found." : "Koi nivesh nahi mile.");
        }
    }

    // ===== утиліти =====

    private static IEnumerable<string> ChunkAsCodeBlocks(List<string> lines, string? title, int maxLen = 3500)
    {
        var chunks = new List<string>();
        var cur = new StringBuilder();
        string? head = title;

        void Flush()
        {
            if (cur.Length == 0) return;
            var msg = (head != null ? head + "\n\n" : "") + "```" + cur.ToString().TrimEnd('\n') + "```";
            chunks.Add(msg);
            cur.Clear();
            head = null; // заголовок лише для першого повідомлення
        }

        foreach (var line in lines)
        {
            // приблизна оцінка довжини з обгорткою
            var prospectiveLen =
                (head?.Length ?? 0) + 4 /*```...```*/ + cur.Length + line.Length + 1 + 2 /*\n\n after title*/;
            if (prospectiveLen > maxLen)
                Flush();
            cur.AppendLine(line);
        }

        Flush();
        return chunks;
    }

    private IEnumerable<string> SplitMessage(string text, int maxLength)
    {
        for (int i = 0; i < text.Length; i += maxLength)
        {
            yield return text.Substring(i, Math.Min(maxLength, text.Length - i));
        }
    }
}