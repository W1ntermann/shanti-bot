using Telegram.Bot;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace ShantiBotDi.Services;

public class SettingsService
{
    private readonly ITelegramBotClient _botClient;
    private readonly IManageService _userManageService;
    private readonly ILocalizationService _localizationService;

    public SettingsService(
        ITelegramBotClient botClient,
        IManageService userManageService,
        ILocalizationService localizationService)
    {
        _botClient = botClient;
        _userManageService = userManageService;
        _localizationService = localizationService;
    }

    public async Task ShowSettingsMenuAsync(long chatId, string language, int? messageId = null)
    {
        var buttons = new List<InlineKeyboardButton[]>
        {
            new[]
            {
                InlineKeyboardButton.WithCallbackData(
                    _localizationService.GetText(language, "settings.language"),
                    "change_language")
            },
            new[]
            {
                InlineKeyboardButton.WithCallbackData(
                    _localizationService.GetText(language, "settings.login"),
                    "change_login")
            },
            new[]
            {
                InlineKeyboardButton.WithCallbackData(
                    _localizationService.GetText(language, "settings.password"),
                    "change_password")
            },
            new[]
            {
                InlineKeyboardButton.WithCallbackData(
                    _localizationService.GetText(language, "settings.wallet"),
                    "change_wallet")
            },
            new[]
            {
                InlineKeyboardButton.WithCallbackData(
                    _localizationService.GetText(language, "settings.backMenu"),
                    "back_to_menu")
            }
        };

        var text = _localizationService.GetText(language, "settings.title");
        var markup = new InlineKeyboardMarkup(buttons);

        if (messageId.HasValue)
        {
            try
            {
                await _botClient.EditMessageText(
                    chatId, messageId.Value, text,
                    parseMode: ParseMode.Markdown,
                    replyMarkup: markup);
                return;
            }
            catch { }
        }
        await _botClient.SendMessage(chatId, text, parseMode: ParseMode.Markdown, replyMarkup: markup);
    }

    public async Task ShowLanguageSelectionAsync(long chatId, string currentLanguage, int? messageId = null)
    {
        var buttons = BuildLanguageButtons("set_language_");
        buttons.Add(
            [
                InlineKeyboardButton.WithCallbackData(
                    _localizationService.GetText(currentLanguage, "settings.backSettings"),
                    "back_to_settings")
            ]);

        var text = _localizationService.GetText(currentLanguage, "settings.languageTitle");
        var markup = new InlineKeyboardMarkup(buttons);

        if (messageId.HasValue)
        {
            try
            {
                await _botClient.EditMessageText(
                    chatId, messageId.Value, text,
                    parseMode: ParseMode.Markdown,
                    replyMarkup: markup);
                return;
            }
            catch { }
        }
        await _botClient.SendMessage(chatId, text, parseMode: ParseMode.Markdown, replyMarkup: markup);
    }

    public async Task AskForNewLoginAsync(long chatId, string currentLanguage)
    {
        await _botClient.SendMessage(
            chatId,
            _localizationService.GetText(currentLanguage, "settings.askLogin"),
            parseMode: ParseMode.Markdown
        );
    }

    public async Task AskForNewPasswordAsync(long chatId, string currentLanguage)
    {
        await _botClient.SendMessage(
            chatId,
            _localizationService.GetText(currentLanguage, "settings.askPassword"),
            parseMode: ParseMode.Markdown
        );
    }

    public async Task AskForNewWalletAddressAsync(long chatId, string currentLanguage)
    {
        await _botClient.SendMessage(
            chatId,
            _localizationService.GetText(currentLanguage, "settings.askWallet"),
            parseMode: ParseMode.Markdown
        );
    }

    public async Task<bool> UpdateUserLanguageAsync(long chatId, string newLanguage)
    {
        var success = await _userManageService.UpdateUserLanguageAsync(chatId, newLanguage);
        var selectedLanguage = _localizationService.GetLanguage(newLanguage);

        if (success)
        {
            await _botClient.SendMessage(
                chatId,
                _localizationService.GetText(newLanguage, "settings.languageUpdatedSuccess", selectedLanguage.NativeName),
                parseMode: ParseMode.Markdown
            );
        }
        else
        {
            await _botClient.SendMessage(
                chatId,
                _localizationService.GetText(newLanguage, "settings.languageUpdatedFailed"),
                parseMode: ParseMode.Markdown
            );
        }

        return success;
    }

    private List<InlineKeyboardButton[]> BuildLanguageButtons(string callbackPrefix)
    {
        var buttons = new List<InlineKeyboardButton[]>();
        var currentRow = new List<InlineKeyboardButton>(2);

        foreach (var language in _localizationService.GetSupportedLanguages())
        {
            currentRow.Add(
                InlineKeyboardButton.WithCallbackData(
                    $"{language.FlagEmoji} {language.NativeName}",
                    $"{callbackPrefix}{language.CallbackSuffix}"));

            if (currentRow.Count == 2)
            {
                buttons.Add(currentRow.ToArray());
                currentRow.Clear();
            }
        }

        if (currentRow.Count > 0)
        {
            buttons.Add(currentRow.ToArray());
        }

        return buttons;
    }

    public async Task<bool> UpdateUserLoginAsync(long chatId, string newLogin)
    {
        var success = await _userManageService.UpdateUserLoginAsync(chatId, newLogin);

        if (success)
        {
            await _botClient.SendMessage(
                chatId,
                $"✅ *LOGIN UPDATED SUCCESSFULLY!*\n\nYour new login is: `{newLogin}`\n\nPlease use this username for future sign-ins."
            );
        }
        else
        {
            await _botClient.SendMessage(
                chatId,
                "❌ *LOGIN UPDATE FAILED*\n\nThis username is already taken or invalid. Please choose a different one.\n\n📝 *Requirements:*\n• 3-20 characters\n• Letters and numbers only\n• Unique username"
            );
        }

        return success;
    }

    public async Task<bool> UpdateUserPasswordAsync(long chatId, string newPassword)
    {
        var success = await _userManageService.UpdateUserPasswordAsync(chatId, newPassword);

        if (success)
        {
            await _botClient.SendMessage(
                chatId,
                "✅ *PASSWORD UPDATED SUCCESSFULLY!*\n\nYour password has been changed successfully.\n\n🔒 Please remember to use a strong, unique password for security."
            );
        }
        else
        {
            await _botClient.SendMessage(
                chatId,
                "❌ *PASSWORD UPDATE FAILED*\n\nUnable to change password at this time. Please try again later.\n\n📞 Contact support if the issue persists."
            );
        }

        return success;
    }

    public async Task<bool> UpdateUserWalletAddressAsync(long chatId, string newWalletAddress)
    {
        var success = await _userManageService.UpdateUserWalletAddressAsync(chatId, newWalletAddress);

        if (success)
        {
            await _botClient.SendMessage(
                chatId,
                $"✅ *WALLET ADDRESS UPDATED SUCCESSFULLY!*\n\nYour new wallet address has been saved:\n`{newWalletAddress}`\n\n⚠️ Please verify that this address is correct for future transactions."
            );
        }
        else
        {
            await _botClient.SendMessage(
                chatId,
                "❌ *WALLET ADDRESS UPDATE FAILED*\n\nUnable to update wallet address at this time. Please try again later.\n\n📞 Contact support if the issue persists."
            );
        }

        return success;
    }
}