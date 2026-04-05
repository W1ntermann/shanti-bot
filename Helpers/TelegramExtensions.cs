using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace ShantiBotDi.Helpers;

public class TelegramExtensions
{
    public static long GetChatId(Update update)
    {
        if (update == null) return 0;
        
        return update.Type switch
        {
            UpdateType.Message => update.Message?.Chat?.Id ?? 0,
            UpdateType.CallbackQuery => update.CallbackQuery?.Message?.Chat?.Id ?? 0,
            _ => 0
        };
    }
}