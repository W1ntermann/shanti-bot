using ShantiBotDi.Models;

namespace ShantiBotDi.Services;

public interface IOperationService
{
    Task<decimal> DepositAsync(long userId, decimal amount);
    Task<decimal> WithdrawAsync(long userId, decimal amount);
    Task CreateDepositAsync(long userId, decimal amount, string currency, string status);
    //Task CreateWithdrawalRequestAsync(long userId, decimal amount);

    Task<(decimal? NewBalance, string ErrorMessage)> CreateInvestmentAsync(long chatId, decimal amount, decimal interestRate, int durationHours);

    Task CreateDepositRequestAsync(long userId, decimal amount);
    Task<List<DepositRequest>> GetPendingDepositRequestsAsync();
    Task<List<WithdrawalRequest>> GetPendingWithdrawalRequestsAsync();
    Task<bool> RejectWithdrawalRequestAsync(int requestId);
    Task<bool> ApproveWithdrawalRequestAsync(int requestId);
    Task<WithdrawalRequest?> GetWithdrawalRequestByIdAsync(int requestId);
    Task<int> CreateWithdrawalRequestAsync(long userId, decimal amount, string walletAddress);
    
}