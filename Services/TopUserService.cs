using Microsoft.EntityFrameworkCore;
using ShantiBotDi.Db;

namespace ShantiBotDi.Services;

public class TopUserService : ITopUserService
{
    private readonly BotDbContext _dbContext;

    public TopUserService(BotDbContext dbContext)
    {
        _dbContext = dbContext;
    }
    
    public async Task<bool> IsTop100UserAsync(long userId)
    {
        var top100Users = await _dbContext.Users
            .Where(u => u.RegistrationDate != null)
            .OrderBy(u => u.RegistrationDate)
            .Take(100)
            .Select(u => u.Id)
            .ToListAsync();

        return top100Users.Contains(userId);
    }
}