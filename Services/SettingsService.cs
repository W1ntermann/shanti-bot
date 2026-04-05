using Telegram.Bot;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace ShantiBotDi.Services;

public class SettingsService
{
    private readonly ITelegramBotClient _botClient;
    private readonly IManageService _userManageService;

    public SettingsService(ITelegramBotClient botClient, IManageService userManageService)
    {
        _botClient = botClient;
        _userManageService = userManageService;
    }

    public async Task ShowSettingsMenuAsync(long chatId, string language, int? messageId = null)
    {
        bool isEnglish = language == "English";

        var buttons = new List<InlineKeyboardButton[]>
        {
            new[]
            {
                InlineKeyboardButton.WithCallbackData(
                    isEnglish ? "🌐 Change Language" : "🌐 Bhasha Badalen",
                    "change_language")
            },
            new[]
            {
                InlineKeyboardButton.WithCallbackData(
                    isEnglish ? "👤 Change Login" : "👤 Login Badalen",
                    "change_login")
            },
            new[]
            {
                InlineKeyboardButton.WithCallbackData(
                    isEnglish ? "🔒 Change Password" : "🔒 Password Badalen",
                    "change_password")
            },
            new[]
            {
                InlineKeyboardButton.WithCallbackData(
                    isEnglish ? "💰 Change Wallet Address" : "💰 Wallet Address Badalen",
                    "change_wallet")
            },
            new[]
            {
                InlineKeyboardButton.WithCallbackData(
                    isEnglish ? "🔙 Back to Main Menu" : "🔙 Mukhya Menu",
                    "back_to_menu")
            }
        };

        var text = isEnglish
            ? "⚙️ *SETTINGS MENU*\n\nWhat would you like to configure?"
            : "⚙️ *SETTINGS MENU*\n\nAap kya configure karna chahenge?";
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
        bool isEnglish = currentLanguage == "English";

        var buttons = new List<InlineKeyboardButton[]>
        {
            new[]
            {
                InlineKeyboardButton.WithCallbackData("🇺🇸 English", "set_language_english"),
                InlineKeyboardButton.WithCallbackData("🇮🇳 Hindi", "set_language_hindi")
            },
            new[]
            {
                InlineKeyboardButton.WithCallbackData(
                    isEnglish ? "↩️ Back to Settings" : "↩️ Settings Wapas",
                    "back_to_settings")
            }
        };

        var text = isEnglish
            ? "🌍 *LANGUAGE SELECTION*\n\nChoose your preferred language:"
            : "🌍 *BHASHA CHUNAV*\n\nApni pasand ki bhasha chunen:";
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
        bool isEnglish = currentLanguage == "English";

        await _botClient.SendMessage(
            chatId,
            isEnglish
                ? "👤 *CHANGE LOGIN*\n\nPlease enter your new username:\n\n📝 *Requirements:*\n• 3-20 characters\n• Letters and numbers only\n• No special characters"
                : "👤 *LOGIN BADALEN*\n\nKripya apna naya username enter karen:\n\n📝 *Requirements:*\n• 3-20 characters\n• Sirf letters aur numbers\n• No special characters",
            parseMode: ParseMode.Markdown
        );
    }

    public async Task AskForNewPasswordAsync(long chatId, string currentLanguage)
    {
        bool isEnglish = currentLanguage == "English";

        await _botClient.SendMessage(
            chatId,
            isEnglish
                ? "🔐 *CHANGE PASSWORD*\n\nPlease enter your new password:\n\n🔒 *Security Recommendations:*\n• Minimum 8 characters\n• Mix of letters and numbers\n• Avoid common passwords"
                : "🔐 *PASSWORD BADALEN*\n\nKripya apna naya password enter karen:\n\n🔒 *Suraksha Salah:*\n• Kam se kam 8 characters\n• Letters aur numbers ka mix\n• Common passwords se bachein",
            parseMode: ParseMode.Markdown
        );
    }

    public async Task AskForNewWalletAddressAsync(long chatId, string currentLanguage)
    {
        bool isEnglish = currentLanguage == "English";

        await _botClient.SendMessage(
            chatId,
            isEnglish
                ? "💰 *CHANGE WALLET ADDRESS*\n\nPlease enter your new wallet address:\n\n📝 *Requirements:*\n• Valid cryptocurrency wallet address\n• Ensure the address is correct\n• Double-check before submitting"
                : "💰 *WALLET ADDRESS BADALEN*\n\nKripya apna naya wallet address enter karen:\n\n📝 *Requirements:*\n• Valid cryptocurrency wallet address\n• Address sahi hone ka dhyan rahe\n• Submit karne se pehle double-check karen",
            parseMode: ParseMode.Markdown
        );
    }

    public async Task<bool> UpdateUserLanguageAsync(long chatId, string newLanguage)
    {
        bool isEnglish = newLanguage == "English";
        var success = await _userManageService.UpdateUserLanguageAsync(chatId, newLanguage);

        if (success)
        {
            await _botClient.SendMessage(
                chatId,
                isEnglish
                    ? "✅ *LANGUAGE UPDATED SUCCESSFULLY!*\n\nYour language preference has been changed to English."
                    : "✅ *BHASHA SAFALTA PURVAK BADALI!*\n\nAapki bhasha Hindi mein badal di gayi hai."
            );
        }
        else
        {
            await _botClient.SendMessage(
                chatId,
                isEnglish
                    ? "❌ *LANGUAGE UPDATE FAILED*\n\nUnable to change language at this time. Please try again later."
                    : "❌ *BHASHA BADALNE MEIN ASAFALTA*\n\nBhasha badalni sambhav nahi hai. Kripya badme prayas karen."
            );
        }

        return success;
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