using Microsoft.EntityFrameworkCore;
using ShantiBotDi.Db;
using ShantiBotDi.Models;

namespace ShantiBotDi.Services;

public class TradingSimulationService : ITradingSimulationService
{
    private readonly BotDbContext _dbContext;
    private readonly Dictionary<long, TradingSession> _sessions = new();
    private const decimal BasePrice = 100m;

    public TradingSimulationService(BotDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task InitializeChartSessionAsync(long telegramId)
    {
        var user = await _dbContext.Users
            .Include(u => u.Investments)
            .FirstOrDefaultAsync(u => u.TelegramId == telegramId);

        if (user == null)
            return;

        // Видаляємо стару сесію
        if (_sessions.ContainsKey(telegramId))
            _sessions.Remove(telegramId);

        // Отримуємо активні інвестиції
        var activeInvestments = user.Investments
            .Where(i => i.IsActive && DateTime.UtcNow < i.StartDate.AddHours(i.DurationHours))
            .ToList();

        var session = new TradingSession
        {
            UserId = user.Id,
            TelegramId = telegramId,
            InitialBalance = user.Balance,
            CurrentBalance = user.Balance,
            SessionStartTime = DateTime.UtcNow,
            Random = new Random(),
            ActiveInvestments = activeInvestments
        };

        // Генеруємо свічки прибутку від 0% до 100% завершення
        GenerateProgressCandles(session);

        _sessions[telegramId] = session;
    }
    
    private void GenerateProgressCandles(TradingSession session)
    {
        session.Candles.Clear();

        if (!session.ActiveInvestments.Any())
            return;

        // Базова ціна = поточний баланс
        decimal basePrice = session.InitialBalance;
        decimal finalBalance = session.InitialBalance;

        // Розраховуємо загальний прибуток від всіх активних інвестицій
        foreach (var inv in session.ActiveInvestments)
        {
            var profit = inv.Amount * (inv.InterestPercent / 100m);
            finalBalance += profit;
        }

        // Генеруємо 20 свічок, які показують прогресію від початку до кінця інвестиції
        int candleCount = 20;
        decimal priceStep = (finalBalance - basePrice) / candleCount;

        for (int i = 0; i < candleCount; i++)
        {
            // Кожна свічка показує хода до наступної контрольної точки
            decimal open = basePrice + (priceStep * i);
            decimal close = basePrice + (priceStep * (i + 1));

            // Додаємо невеликі коливання для реалістичності
            decimal volatility = (decimal)(session.Random.NextDouble() - 0.5) * 0.5m;
            close += volatility;

            var high = Math.Max(open, close) * (1 + (decimal)session.Random.NextDouble() * 0.001m);
            var low = Math.Min(open, close) * (1 - (decimal)session.Random.NextDouble() * 0.001m);

            var candle = new CandleData
            {
                Open = open,
                Close = close,
                High = high,
                Low = low,
                Volume = (long)(session.Random.Next(1000000, 3000000)),
                Time = DateTime.UtcNow.AddMinutes(i * 2) // Кожні 2 хвилини
            };

            session.Candles.Add(candle);
        }

        // Остання свічка = фінальний баланс
        var lastCandle = session.Candles.Last();
        var finalCandle = new CandleData
        {
            Open = lastCandle.Close,
            Close = finalBalance,
            High = Math.Max(lastCandle.Close, finalBalance),
            Low = Math.Min(lastCandle.Close, finalBalance),
            Volume = 5000000,
            Time = DateTime.UtcNow.AddMinutes(candleCount * 2)
        };
        session.Candles[session.Candles.Count - 1] = finalCandle;
    }
    public async Task<List<CandleData>> GenerateChartCandlesAsync(long telegramId)
    {
        if (!_sessions.TryGetValue(telegramId, out var session))
        {
            await InitializeChartSessionAsync(telegramId);
            if (!_sessions.TryGetValue(telegramId, out session))
                return new List<CandleData>();
        }

        return session.Candles;
    }
    
    public async Task<TradingMetrics> GetChartMetricsAsync(long telegramId)
    {
        if (!_sessions.TryGetValue(telegramId, out var session))
        {
            return new TradingMetrics { CandleCount = 0 };
        }

        if (session.Candles.Count == 0)
        {
            return new TradingMetrics { CandleCount = 0 };
        }

        var firstCandle = session.Candles.First();
        var lastCandle = session.Candles.Last();

        decimal startPrice = firstCandle.Open;
        decimal endPrice = lastCandle.Close;
        decimal highPrice = session.Candles.Max(c => c.High);
        decimal lowPrice = session.Candles.Min(c => c.Low);

        // Прибуток від початку до кінця
        decimal totalProfit = endPrice - startPrice;
        decimal profitPercent = session.InitialBalance > 0 
            ? (totalProfit / session.InitialBalance) * 100 
            : 0;

        // Оновлюємо баланс сесії на фінальне значення
        session.CurrentBalance = endPrice;

        return new TradingMetrics
        {
            CurrentPrice = endPrice,
            HighPrice = highPrice,
            LowPrice = lowPrice,
            ChangePercent = profitPercent,
            BalanceChangePercent = profitPercent, // Для користувача це один і той же показник
            ProfitLoss = totalProfit,
            CandleCount = session.Candles.Count
        };
    }

    public void ClearSession(long telegramId)
    {
        if (_sessions.ContainsKey(telegramId))
            _sessions.Remove(telegramId);
    }
}

