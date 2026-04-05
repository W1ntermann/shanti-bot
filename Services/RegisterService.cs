using Microsoft.EntityFrameworkCore;
using ShantiBotDi.Db;
using ShantiBotDi.Models;
using Telegram.Bot;

namespace ShantiBotDi.Services;

public class RegisterService : IRegisterService
{
    private readonly BotDbContext _db;
    private readonly IAuthService _authService;
    private readonly ITelegramBotClient _telegramBotClient;
    private readonly ITopUserService _topUserService;
    private readonly IQuestService _questService;

    public RegisterService(BotDbContext db, IAuthService authService, 
        ITelegramBotClient telegramBotClient, ITopUserService topUserService,
        IQuestService questService)
    {
        _db = db;
        _authService = authService;
        _telegramBotClient = telegramBotClient;
        _topUserService = topUserService;
        _questService = questService;
    }

    public async Task<(bool Success, string Message)> RegisterUserAsync(
        long telegramId, string username, string password, string walletAddress, string language, string? referralCode = null)
    {
        var existingUser = await _db.Users
            .FirstOrDefaultAsync(u => u.TelegramId == telegramId);

        // ВИПРАВЛЕННЯ: використовуємо мову з параметра для нових користувачів
        var lang = existingUser?.Language ?? language;

        // 🔹 Якщо юзер існує і ще не завершив реєстрацію
        if (existingUser != null && existingUser.IsTemporary)
        {
            existingUser.Username = username;
            existingUser.PasswordHash = _authService.HashPassword(password);
            existingUser.WalletAddress = walletAddress;
            existingUser.IsAuthorized = true;
            existingUser.Language = language;
            existingUser.IsTemporary = false;

            // ✅ Якщо був PendingReferralCode – обробляємо
            if (!string.IsNullOrEmpty(existingUser.PendingReferralCode))
            {
                await ProcessReferralCodeAsync(existingUser, existingUser.PendingReferralCode);
                existingUser.PendingReferralCode = null;
            }

            await _db.SaveChangesAsync();
            
        }

        // 🔹 Якщо користувача немає — створюємо нового
        if (existingUser == null)
        {
            var newUser = new User
            {
                TelegramId = telegramId,
                Username = username,
                PasswordHash = _authService.HashPassword(password),
                WalletAddress = walletAddress,
                IsAuthorized = true,
                Role = UserRole.User,
                RegistrationDate = DateTime.UtcNow,
                Language = language
            };

            if (!string.IsNullOrEmpty(referralCode))
            {
                await ProcessReferralCodeAsync(newUser, referralCode);
            }

            _db.Users.Add(newUser);
            await _db.SaveChangesAsync();
            
            // ВИПРАВЛЕННЯ: використовуємо мову нового користувача
            return (true, language == "English"
                ? "✅ Registration successful! You can now use the bot."
                : "✅ Registration safal raha! Ab aap bot ka istemal kar sakte hain.");
        }

        return (false, "❌ Something went wrong.");
    }

    private async Task ProcessReferralCodeAsync(User newUser, string referralCode)
    {
        // ВИПРАВЛЕННЯ: безпечне порівняння без урахування регістру
        var referrer = await _db.Users
            .FirstOrDefaultAsync(u => u.ReferralCode != null && 
                                      u.ReferralCode.ToUpper() == referralCode.ToUpper());

        if (referrer != null)
        {
            // ВИПРАВЛЕННЯ: перевірка на самозапрошення
            if (referrer.Id == newUser.Id)
            {
                return; // Не дозволяємо самозапрошення
            }

            newUser.ReferredBy = referrer.Id;
            

            // ВИПРАВЛЕННЯ: уникнення фіксованої суми $5
            string message = referrer.Language == "English"
                ? $"🎉 New user {newUser.Username} registered using your referral link!\n\n" +
                  $"💰 You will earn 10% from their investment profits!\n" +
                  $"🔥 Keep inviting more friends to earn more!"
                : $"🎉 Naya user {newUser.Username} aapke referral link se register hua!\n\n" +
                  $"💰 Aapko unke investment profits ka 10% milega!\n" +
                  $"🔥 Zyada kamane ke liye aur dosto ko invite karein!";

            await _telegramBotClient.SendMessage(referrer.TelegramId, message);
        }
    }
}