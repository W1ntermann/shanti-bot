using ShantiBotDi.Models;

namespace ShantiBotDi.Services;

public interface IQuestService
{
    Task<List<Quest>> GetDailyQuestsAsync();
    Task<List<UserQuest>> GetUserQuestsAsync(long telegramId);
    Task<bool> UpdateQuestProgressAsync(long telegramId, int questId, decimal progressToAdd = 1);
    Task<(bool success, decimal reward)> ClaimQuestRewardAsync(long telegramId, int questId);
    Task UpdateUserProgressAutomaticallyAsync(long telegramId);
}