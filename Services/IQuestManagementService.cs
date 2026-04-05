using ShantiBotDi.Models;

namespace ShantiBotDi.Services;

public interface IQuestManagementService
{
    Task InitializeDailyQuestsAsync();
    Task<List<Quest>> GetAllQuestsAsync();
    Task<Quest> CreateQuestAsync(string title, string description, decimal reward, bool isDaily = true);
    Task<bool> DeleteQuestAsync(int questId);
    Task<bool> UpdateQuestAsync(int questId, string title, string description, decimal reward);
    Task<bool> DeactivateQuestAsync(int questId);
}