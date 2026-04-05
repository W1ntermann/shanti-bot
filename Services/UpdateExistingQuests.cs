using Microsoft.EntityFrameworkCore;
using ShantiBotDi.Db;
using ShantiBotDi.Models;

namespace ShantiBotDi.Services;

public class UpdateExistingQuests
{
     public static async Task Run(BotDbContext dbContext)
    {
        Console.WriteLine("=== ОНОВЛЕННЯ ІСНУЮЧИХ КВЕСТІВ ===");
        
        var allQuests = await dbContext.Quests.ToListAsync();
        Console.WriteLine($"Знайдено квестів: {allQuests.Count}");
        
        foreach (var quest in allQuests)
        {
            // Встановлюємо значення за замовчуванням для нових полів
            if (string.IsNullOrEmpty(quest.TargetType))
            {
                quest.TargetType = GetDefaultTargetType(quest);
                quest.TargetValue = GetDefaultTargetValue(quest);
                quest.ProgressUnit = GetDefaultProgressUnit(quest);
                
                Console.WriteLine($"Оновлено квест: {quest.Title}");
                Console.WriteLine($"  TargetType: {quest.TargetType}");
                Console.WriteLine($"  TargetValue: {quest.TargetValue}");
                Console.WriteLine($"  ProgressUnit: {quest.ProgressUnit}");
            }
        }
        
        await dbContext.SaveChangesAsync();
        Console.WriteLine("✅ Квести оновлено успішно!");
    }
    
    private static string GetDefaultTargetType(Quest quest)
    {
        // Автоматично визначаємо тип на основі назви
        var title = quest.Title.ToLower();
        
        if (title.Contains("balance") || title.Contains("booster"))
            return "balance";
        if (title.Contains("investment") || title.Contains("invest"))
            return "investment";
        if (title.Contains("profit") || title.Contains("earn"))
            return "profit";
        if (title.Contains("referral") || title.Contains("invite"))
            return "referral";
        if (title.Contains("daily") || title.Contains("login"))
            return "manual";
        
        return "manual";
    }
    
    private static decimal GetDefaultTargetValue(Quest quest)
    {
        var title = quest.Title.ToLower();
        
        if (title.Contains("500") || title.Contains("balance"))
            return 500;
        if (title.Contains("100") || title.Contains("profit"))
            return 100;
        if (title.Contains("7") || title.Contains("weekly"))
            return 7;
        
        return 1;
    }
    
    private static string GetDefaultProgressUnit(Quest quest)
    {
        var targetType = GetDefaultTargetType(quest);
        
        return targetType switch
        {
            "balance" => "USDT",
            "investment" => "investments",
            "profit" => "USDT",
            "referral" => "friends",
            "manual" => "times",
            _ => "times"
        };
    }
}