using Microsoft.EntityFrameworkCore;
using ShantiBotDi.Db;
using ShantiBotDi.Models;
using Telegram.Bot;

namespace ShantiBotDi.Services;

public class OperationService : IOperationService
{
    private readonly BotDbContext _botDbContext;
    private readonly ITelegramBotClient _telegramBotClient;
    private readonly IQuestService _questService;

    public OperationService(BotDbContext context, ITelegramBotClient telegramBotClient, IQuestService questService)
    {
        _botDbContext = context;
        _telegramBotClient = telegramBotClient;
        _questService = questService;
    }

    public async Task<decimal> DepositAsync(long userId, decimal amount)
    {
        var user = await _botDbContext.Users.FirstOrDefaultAsync(u => u.TelegramId == userId);
        if (user == null)
            throw new Exception("User not found");

        user.Balance += amount;
        await _botDbContext.SaveChangesAsync();
        return user.Balance;
    }

    public async Task<decimal> WithdrawAsync(long userId, decimal amount)
    {
        var user = await _botDbContext.Users.FirstOrDefaultAsync(u => u.TelegramId == userId);
        if (user == null)
            throw new Exception("User not found");

        if (user.Balance < amount)
            throw new Exception("Not enough funds");

        user.Balance -= amount;

        // Створюємо заявку на вивід
        var withdrawalRequest = new WithdrawalRequest
        {
            UserId = user.Id,
            Amount = amount,
            CreatedAt = DateTime.UtcNow,
            Status = "Pending",
            IsFullWithdrawal = false
        };
        _botDbContext.WithdrawalRequests.Add(withdrawalRequest);

        await _botDbContext.SaveChangesAsync();

        return user.Balance;
    }


    public async Task<(decimal? NewBalance, string ErrorMessage)> CreateInvestmentAsync(
        long chatId, decimal amount, decimal interestRate, int durationHours)
    {
        await using var transaction = await _botDbContext.Database.BeginTransactionAsync();
        try
        {
            var user = await _botDbContext.Users.FirstOrDefaultAsync(u => u.TelegramId == chatId);
            if (user == null)
                return (null, "User not found");

            if (amount <= 0)
                return (null, "Investment amount must be positive");

            if (user.Balance < amount)
                return (null, $"Insufficient balance. Current: {user.Balance:F2} USDT, Required: {amount:F2} USDT");

            // Віднімаємо суму з балансу
            user.Balance -= amount;

            var startDate = DateTime.UtcNow;

            var investment = new Investment
            {
                UserId = user.Id,
                Amount = amount,
                InterestPercent = interestRate,
                DurationHours = durationHours,
                StartDate = startDate,
                EndDate = startDate.AddHours(durationHours),
                IsActive = true,
                ProfitAddedToBalance = false
            };

            _botDbContext.Investments.Add(investment);
            await _botDbContext.SaveChangesAsync();
        
            // 🔴 ОНОВЛЕННЯ КВЕСТІВ - викликаємо до коміту транзакції
            
            await transaction.CommitAsync();

            return (user.Balance, null); // Успіх
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            Console.WriteLine($"Error creating investment: {ex.Message}");
            return (null, $"System error: {ex.Message}");
        }
    }

    public async Task CreateDepositAsync(long userId, decimal amount, string currency, string status)
    {
        var user = await _botDbContext.Users.FirstOrDefaultAsync(u => u.TelegramId == userId);
        if (user == null)
            throw new Exception("User not found");

        var deposit = new Deposit
        {
            UserId = user.Id,
            Amount = amount,
            Currency = currency,
            Status = status,
            CreatedAt = DateTime.UtcNow
        };

        _botDbContext.Deposits.Add(deposit);

        await _botDbContext.SaveChangesAsync();
    }

    public async Task<int> CreateWithdrawalRequestAsync(long userId, decimal amount, string walletAddress)
    {
        var user = await _botDbContext.Users
            .FirstOrDefaultAsync(u => u.TelegramId == userId);

        if (user == null)
            throw new InvalidOperationException($"User with TelegramId {userId} not found.");

        if (amount > user.Balance)
            throw new InvalidOperationException("Insufficient balance");

        if (string.IsNullOrEmpty(walletAddress))
            throw new InvalidOperationException("Wallet address not set");

        var request = new WithdrawalRequest
        {
            UserId = user.Id,
            User = user,
            Amount = amount,
            Status = "Pending",
            CreatedAt = DateTime.UtcNow,
            IsFullWithdrawal = false
        };

        _botDbContext.WithdrawalRequests.Add(request);
        await _botDbContext.SaveChangesAsync();

        return request.Id;
    }

    public async Task CreateDepositRequestAsync(long userId, decimal amount)
    {
        var user = await _botDbContext.Users
            .FirstOrDefaultAsync(u => u.TelegramId == userId);

        if (user == null)
            throw new InvalidOperationException($"User with TelegramId {userId} not found.");

        var request = new DepositRequest
        {
            UserId = user.Id,
            User = user,
            Amount = amount,
            Status = "Pending",
            CreatedAt = DateTime.UtcNow
        };

        _botDbContext.DepositRequests.Add(request);
        await _botDbContext.SaveChangesAsync();
    }

    public async Task<List<DepositRequest>> GetPendingDepositRequestsAsync()
    {
        return await _botDbContext.DepositRequests
            .Where(r => r.Status == "Pending")
            .ToListAsync();
    }

    public async Task<List<WithdrawalRequest>> GetPendingWithdrawalRequestsAsync()
    {
        return await _botDbContext.WithdrawalRequests
            .Include(w => w.User)
            .Where(w => w.Status == "Pending")
            .ToListAsync();
    }

    public async Task<bool> ApproveWithdrawalRequestAsync(int requestId)
    {
        var request = await _botDbContext.WithdrawalRequests.FindAsync(requestId);
        if (request == null || request.Status != "Pending")
            return false;

        request.Status = "Approved";
        await _botDbContext.SaveChangesAsync();
        return true;
    }

    public async Task<bool> RejectWithdrawalRequestAsync(int requestId)
    {
        var request = await _botDbContext.WithdrawalRequests.FindAsync(requestId);
        if (request == null || request.Status != "Pending")
            return false;

        request.Status = "Rejected";
        await _botDbContext.SaveChangesAsync();
        return true;
    }

    public async Task<WithdrawalRequest?> GetWithdrawalRequestByIdAsync(int requestId)
    {
        return await _botDbContext.WithdrawalRequests
            .Include(r => r.User)
            .FirstOrDefaultAsync(r => r.Id == requestId);
    }
    
}