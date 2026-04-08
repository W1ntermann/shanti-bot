using System.Globalization;

namespace ShantiBotDi.Services;

public static class BotLanguageCodes
{
    public const string English = "en";
    public const string Hinglish = "hinglish";
    public const string Russian = "ru";
    public const string Farsi = "fa";
    public const string Arabic = "ar";
    public const string SimplifiedChinese = "zh-Hans";
}

public sealed record SupportedBotLanguage(
    string Code,
    string LegacyDisplayName,
    string EnglishName,
    string NativeName,
    string FlagEmoji,
    string CallbackSuffix,
    bool IsRtl = false);

public interface ILocalizationService
{
    IReadOnlyList<SupportedBotLanguage> GetSupportedLanguages();
    SupportedBotLanguage GetLanguage(string? language);
    bool TryGetLanguageByCallback(string callbackSuffix, out SupportedBotLanguage language);
    string NormalizeCode(string? language);
    string NormalizeDisplayName(string? language);
    bool IsEnglish(string? language);
    bool IsRtl(string? language);
    string GetText(string? language, string key, params object[] args);
    string FormatText(string? language, string text);
    string GetLanguageSelectionWelcomeText();
}

public class LocalizationService : ILocalizationService
{
    private const char RightToLeftMark = '\u200F';

    private static readonly IReadOnlyList<SupportedBotLanguage> SupportedLanguages =
    [
        new(BotLanguageCodes.English, "English", "English", "English", "🇬🇧", "english"),
        new(BotLanguageCodes.Hinglish, "Hinglish", "Hinglish", "Hinglish", "🇮🇳", "hinglish"),
        new(BotLanguageCodes.Russian, "Russian", "Russian", "Русский", "🇷🇺", "russian"),
        new(BotLanguageCodes.Farsi, "Farsi", "Farsi", "فارسی", "🇮🇷", "farsi", true),
        new(BotLanguageCodes.Arabic, "Arabic", "Modern Standard Arabic", "العربية", "🇸🇦", "arabic", true),
        new(BotLanguageCodes.SimplifiedChinese, "Chinese", "Simplified Chinese", "简体中文", "🇨🇳", "chinese")
    ];

    private static readonly Dictionary<string, Dictionary<string, string>> Resources =
        new(StringComparer.OrdinalIgnoreCase)
        {
            [BotLanguageCodes.English] = new(StringComparer.OrdinalIgnoreCase)
            {
                ["language.saved"] = "✅ Language saved",
                ["auth.register"] = "📝 Register",
                ["auth.login"] = "🔑 Login",
                ["auth.chooseAction"] = "Choose an action:",
                ["auth.continuePrompt"] = "Please register or login to continue",
                ["auth.passwordPrompt"] = "🔑 Enter your password to login:",
                ["registration.enterUsername"] = "✏️ Please enter your desired username:",
                ["registration.rulesAck"] = "✅ I have read",
                ["registration.ready"] = "👍 Great! Now you're all set to start. Welcome to our platform!",
                ["registration.success"] = "✅ Registration successful! You can now use the bot.",
                ["registration.referral"] = "🎉 New user {0} registered using your referral link!\n\n💰 You will earn 10% from their investment profits!\n🔥 Keep inviting more friends to earn more!",
                ["registration.rules"] = @"📜 *Registration & Payment Instructions — StarQuantum.AI*

Step 1 — Create Your Login Details
• Username: choose a unique name you will remember (avoid using your real name for privacy)
• Password: at least 8 characters, mixing letters, numbers, and symbols. Never share it with anyone

Step 2 — Enter Your Payment Wallet Address
• In the field ""Payment Wallet (USDT - TRC20)"", enter the wallet address you will use to send funds
• Only USDT in TRC20 network is accepted. Sending from another network will result in permanent loss of funds
• Double-check the address before sending - one wrong character and the payment will not arrive

Step 3 — Funds Linking
• All top-ups made from the wallet you entered will be automatically credited to your StarQuantum.AI balance
• Refunds (if necessary) will only be sent back to the same wallet address from which the funds were originally sent

Step 4 — Confirmation
• After registration, you will gain secure access to the StarQuantum.AI panel
• StarQuantum.AI will never ask for your seed phrase or private keys. Your funds remain fully under your control",
                ["settings.title"] = "⚙️ *SETTINGS MENU*\n\nWhat would you like to configure?",
                ["settings.language"] = "🌐 Change Language",
                ["settings.login"] = "👤 Change Login",
                ["settings.password"] = "🔒 Change Password",
                ["settings.wallet"] = "💰 Change Wallet Address",
                ["settings.backMenu"] = "🔙 Back to Main Menu",
                ["settings.languageTitle"] = "🌍 *LANGUAGE SELECTION*\n\nChoose your preferred language:",
                ["settings.backSettings"] = "↩️ Back to Settings",
                ["settings.askLogin"] = "👤 *CHANGE LOGIN*\n\nPlease enter your new username:\n\n📝 *Requirements:*\n• 3-20 characters\n• Letters and numbers only\n• No special characters",
                ["settings.askPassword"] = "🔐 *CHANGE PASSWORD*\n\nPlease enter your new password:\n\n🔒 *Security Recommendations:*\n• Minimum 8 characters\n• Mix of letters and numbers\n• Avoid common passwords",
                ["settings.askWallet"] = "💰 *CHANGE WALLET ADDRESS*\n\nPlease enter your new wallet address:\n\n📝 *Requirements:*\n• Valid cryptocurrency wallet address\n• Ensure the address is correct\n• Double-check before submitting",
                ["settings.languageUpdatedSuccess"] = "✅ *LANGUAGE UPDATED SUCCESSFULLY!*\n\nYour language preference has been changed to {0}.",
                ["settings.languageUpdatedFailed"] = "❌ *LANGUAGE UPDATE FAILED*\n\nUnable to change language at this time. Please try again later.",
                ["menu.welcome"] = "👋 <b>Welcome, {0}!</b>\n\nI'm <b>Shanti</b>, your AI investment assistant.\nHow can I help you today?",
                ["menu.deposit"] = "Deposit",
                ["menu.invest"] = "Invest",
                ["menu.profile"] = "My Profile",
                ["menu.withdraw"] = "Withdraw",
                ["menu.referral"] = "Referral Rewards",
                ["menu.quests"] = "Quests",
                ["menu.about"] = "About StarQuantum.AI",
                ["menu.support"] = "Support & Help",
                ["menu.settings"] = "Settings",
                ["maintenance.start"] = "🔧 <b>Technical Maintenance</b>\n\nBot temporarily unavailable.\n<b>Reason:</b> {0}\n\nPlease wait!",
                ["maintenance.end"] = "✅ <b>Bot Restored</b>\n\nBot is now available and working normally!\nThank you for waiting!",
                ["delay.defaultReason"] = "market conditions",
                ["delay.message"] = "<b>📢 Important Update</b>\n\nDear Trader,\n\nDue to {0}, there is a 5-day delay in payouts from the exchange we work with. This is a temporary situation and we are working to resolve the issue.\n\n<b>✅ For your patience:</b>\nWe have added <b>3 USDT bonus</b> to your account! You can use this amount for trading or withdrawal.\n\n🙏 Thank you for your understanding and support!\n\n<b>🔜 Updates:</b>\nWe will notify you as soon as the situation returns to normal.\n\n—\nStarQuantum.AI Team ❤️",
                ["chart.title"] = "💰 Projected Balance",
                ["chart.profit"] = "Profit:",
                ["chart.balance"] = "New Balance:",
                ["chart.legend"] = "📈 Green candles represent your profit growth trajectory"
            },
            [BotLanguageCodes.Hinglish] = new(StringComparer.OrdinalIgnoreCase)
            {
                ["language.saved"] = "✅ Bhasha save ki gayi",
                ["auth.register"] = "📝 Register karein",
                ["auth.login"] = "🔑 Login karein",
                ["auth.chooseAction"] = "Action chuno:",
                ["auth.continuePrompt"] = "Aage badhne ke liye register ya login karein",
                ["auth.passwordPrompt"] = "🔑 Login karne ke liye password dalo:",
                ["registration.enterUsername"] = "✏️ Apna username likho:",
                ["registration.rulesAck"] = "✅ Main samjha",
                ["registration.ready"] = "👍 Shandaar! Ab aap shuru karne ke liye taiyaar hain. Platform mein aapka swagat hai!",
                ["registration.success"] = "✅ Registration safal raha! Ab aap bot ka istemal kar sakte hain.",
                ["registration.referral"] = "🎉 Naya user {0} aapke referral link se register hua!\n\n💰 Aapko unke investment profits ka 10% milega!\n🔥 Zyada kamane ke liye aur dosto ko invite karein!",
                ["registration.rules"] = @"📜 Registration aur Payment Instructions — StarQuantum.AI

Step 1 — Apna Login Banaye
• Username: unique naam rakhe (apna asli naam na use kare)
• Password: kam se kam 8 characters, letters, numbers aur symbols ka mix. Kisi ko na bataye

Step 2 — Apna Payment Wallet Address Daale
• ""Payment Wallet (USDT - TRC20)"" mein woh wallet address daale jisme se paise bhejoge
• Sirf USDT TRC20 network accept hota hai. Dusre network se paise bhejoge toh kho jayenge
• Address double-check karein - ek galat character se payment nahi pahuchegi

Step 3 — Paise Link Karne Ka Tarika
• Is wallet se kiya gaya har top-up aapke StarQuantum.AI balance mein automatically add hoga
• Refund (agar zaroori ho) sirf usi wallet par bheja jayega jahan se paise aaye the

Step 4 — Confirmation
• Registration ke baad, StarQuantum.AI panel tak secure access milega
• StarQuantum.AI kabhi aapka seed phrase ya private keys nahi mangega. Aapke paise hamesha aapke control mein rahenge",
                ["settings.title"] = "⚙️ *SETTINGS MENU*\n\nAap kya configure karna chahenge?",
                ["settings.language"] = "🌐 Bhasha Badalen",
                ["settings.login"] = "👤 Login Badalen",
                ["settings.password"] = "🔒 Password Badalen",
                ["settings.wallet"] = "💰 Wallet Address Badalen",
                ["settings.backMenu"] = "🔙 Mukhya Menu",
                ["settings.languageTitle"] = "🌍 *BHASHA CHUNAV*\n\nApni pasand ki bhasha chunen:",
                ["settings.backSettings"] = "↩️ Settings Wapas",
                ["settings.askLogin"] = "👤 *LOGIN BADALEN*\n\nKripya apna naya username enter karen:\n\n📝 *Requirements:*\n• 3-20 characters\n• Sirf letters aur numbers\n• No special characters",
                ["settings.askPassword"] = "🔐 *PASSWORD BADALEN*\n\nKripya apna naya password enter karen:\n\n🔒 *Suraksha Salah:*\n• Kam se kam 8 characters\n• Letters aur numbers ka mix\n• Common passwords se bachein",
                ["settings.askWallet"] = "💰 *WALLET ADDRESS BADALEN*\n\nKripya apna naya wallet address enter karen:\n\n📝 *Requirements:*\n• Valid cryptocurrency wallet address\n• Address sahi hone ka dhyan rahe\n• Submit karne se pehle double-check karen",
                ["settings.languageUpdatedSuccess"] = "✅ *BHASHA SAFALTA PURVAK BADALI!*\n\nAapki bhasha {0} mein badal di gayi hai.",
                ["settings.languageUpdatedFailed"] = "❌ *BHASHA BADALNE MEIN ASAFALTA*\n\nBhasha badalni sambhav nahi hai. Kripya baad mein prayas karen.",
                ["menu.welcome"] = "👋 <b>Aapka swagat hai, {0}!</b>\n\nMain <b>Shanti</b> hoon, aapka AI nivesh sahayak.\nAaj main aapki kya madad kar sakta hoon?",
                ["menu.deposit"] = "Jama Karein",
                ["menu.invest"] = "Nivesh Karein",
                ["menu.profile"] = "Mera Profile",
                ["menu.withdraw"] = "Nikalna",
                ["menu.referral"] = "Referral Inaam",
                ["menu.quests"] = "Quests",
                ["menu.about"] = "StarQuantum.AI Ke Bare Me",
                ["menu.support"] = "Sahayata",
                ["menu.settings"] = "Settings",
                ["maintenance.start"] = "🔧 <b>Technical Maintenance</b>\n\nBot temporarily unavailable hai.\n<b>Reason:</b> {0}\n\nPlease wait karo!",
                ["maintenance.end"] = "✅ <b>Bot Restored</b>\n\nBot ab available hai and working normally!\nThank you for waiting!",
                ["delay.defaultReason"] = "market conditions ke karan",
                ["delay.message"] = "<b>📢 Important Update</b>\n\nDear Trader,\n\nMarket {0} exchange se payouts me 5 days ki delay ho rahi hai. Yeh situation temporary hai aur hum issue resolve karne me lage huye hain.\n\n<b>✅ Aapke patience ke liye:</b>\nHum aapke account me <b>3 USDT bonus</b> add kar diye hain! Yeh amount aap trading ya withdrawal ke liye use kar sakte hain.\n\n🙏 Aapke support ke liye dhanyavaad!\n\n<b>🔜 Updates:</b>\nJaisi hi situation normal hogi, hum aapko notify kar denge.\n\n—\nStarQuantum.AI Team ❤️",
                ["chart.title"] = "💰 Expected Balance",
                ["chart.profit"] = "Profit:",
                ["chart.balance"] = "New Balance:",
                ["chart.legend"] = "📈 Green candles show your profit growth"
            },
            [BotLanguageCodes.Russian] = new(StringComparer.OrdinalIgnoreCase)
            {
                ["language.saved"] = "✅ Язык сохранен",
                ["auth.register"] = "📝 Регистрация",
                ["auth.login"] = "🔑 Вход",
                ["auth.chooseAction"] = "Выберите действие:",
                ["auth.continuePrompt"] = "Пожалуйста, зарегистрируйтесь или войдите, чтобы продолжить",
                ["auth.passwordPrompt"] = "🔑 Введите пароль для входа:",
                ["registration.enterUsername"] = "✏️ Введите желаемое имя пользователя:",
                ["registration.rulesAck"] = "✅ Я прочитал",
                ["registration.ready"] = "👍 Отлично! Теперь все готово к старту. Добро пожаловать на платформу!",
                ["registration.success"] = "✅ Регистрация прошла успешно! Теперь вы можете пользоваться ботом.",
                ["registration.referral"] = "🎉 Новый пользователь {0} зарегистрировался по вашей реферальной ссылке!\n\n💰 Вы получите 10% от его инвестиционной прибыли!\n🔥 Приглашайте больше друзей, чтобы зарабатывать больше!",
                ["registration.rules"] = @"📜 *Инструкция по регистрации и оплате — StarQuantum.AI*

Шаг 1 — Создайте данные для входа
• Имя пользователя: выберите уникальное имя, которое вы запомните
• Пароль: минимум 8 символов, используйте буквы, цифры и символы. Никому его не сообщайте

Шаг 2 — Укажите адрес платежного кошелька
• В поле ""Payment Wallet (USDT - TRC20)"" введите адрес кошелька, с которого будете отправлять средства
• Принимается только USDT в сети TRC20. Отправка из другой сети приведет к безвозвратной потере средств
• Внимательно проверьте адрес перед отправкой

Шаг 3 — Привязка пополнений
• Все пополнения с указанного кошелька будут автоматически зачисляться на ваш баланс StarQuantum.AI
• Возврат средств, если он потребуется, будет отправлен только на тот же кошелек

Шаг 4 — Подтверждение
• После регистрации вы получите защищенный доступ к панели StarQuantum.AI
• StarQuantum.AI никогда не попросит seed-фразу или приватные ключи. Средства остаются под вашим контролем",
                ["settings.title"] = "⚙️ *МЕНЮ НАСТРОЕК*\n\nЧто вы хотите настроить?",
                ["settings.language"] = "🌐 Сменить язык",
                ["settings.login"] = "👤 Сменить логин",
                ["settings.password"] = "🔒 Сменить пароль",
                ["settings.wallet"] = "💰 Сменить адрес кошелька",
                ["settings.backMenu"] = "🔙 Назад в главное меню",
                ["settings.languageTitle"] = "🌍 *ВЫБОР ЯЗЫКА*\n\nВыберите предпочтительный язык:",
                ["settings.backSettings"] = "↩️ Назад в настройки",
                ["settings.askLogin"] = "👤 *СМЕНА ЛОГИНА*\n\nВведите новый логин:\n\n📝 *Требования:*\n• 3-20 символов\n• Только буквы и цифры\n• Без специальных символов",
                ["settings.askPassword"] = "🔐 *СМЕНА ПАРОЛЯ*\n\nВведите новый пароль:\n\n🔒 *Рекомендации по безопасности:*\n• Минимум 8 символов\n• Комбинация букв и цифр\n• Избегайте простых паролей",
                ["settings.askWallet"] = "💰 *СМЕНА АДРЕСА КОШЕЛЬКА*\n\nВведите новый адрес кошелька:\n\n📝 *Требования:*\n• Корректный адрес криптокошелька\n• Убедитесь, что адрес введен правильно\n• Проверьте его еще раз перед отправкой",
                ["settings.languageUpdatedSuccess"] = "✅ *ЯЗЫК УСПЕШНО ОБНОВЛЕН!*\n\nТеперь язык интерфейса: {0}.",
                ["settings.languageUpdatedFailed"] = "❌ *НЕ УДАЛОСЬ ОБНОВИТЬ ЯЗЫК*\n\nСейчас невозможно изменить язык. Попробуйте позже.",
                ["menu.welcome"] = "👋 <b>Добро пожаловать, {0}!</b>\n\nЯ <b>Shanti</b>, ваш AI-помощник по инвестициям.\nЧем я могу помочь сегодня?",
                ["menu.deposit"] = "Пополнить",
                ["menu.invest"] = "Инвестировать",
                ["menu.profile"] = "Мой профиль",
                ["menu.withdraw"] = "Вывести",
                ["menu.referral"] = "Реферальные награды",
                ["menu.quests"] = "Квесты",
                ["menu.about"] = "О StarQuantum.AI",
                ["menu.support"] = "Поддержка",
                ["menu.settings"] = "Настройки",
                ["maintenance.start"] = "🔧 <b>Техническое обслуживание</b>\n\nБот временно недоступен.\n<b>Причина:</b> {0}\n\nПожалуйста, подождите!",
                ["maintenance.end"] = "✅ <b>Работа бота восстановлена</b>\n\nБот снова доступен и работает нормально!\nСпасибо за ожидание!",
                ["delay.defaultReason"] = "рыночных условий",
                ["delay.message"] = "<b>📢 Важное обновление</b>\n\nУважаемый трейдер,\n\nИз-за {0} выплаты от биржи задерживаются на 5 дней. Ситуация временная, и мы уже работаем над ее решением.\n\n<b>✅ За ваше терпение:</b>\nМы начислили <b>3 USDT бонуса</b> на ваш аккаунт! Эти средства можно использовать для торговли или вывода.\n\n🙏 Спасибо за понимание и поддержку!\n\n<b>🔜 Обновления:</b>\nМы сообщим вам, как только ситуация нормализуется.\n\n—\nКоманда StarQuantum.AI ❤️",
                ["chart.title"] = "💰 Прогнозируемый баланс",
                ["chart.profit"] = "Прибыль:",
                ["chart.balance"] = "Новый баланс:",
                ["chart.legend"] = "📈 Зеленые свечи показывают траекторию роста вашей прибыли"
            },
            [BotLanguageCodes.Farsi] = new(StringComparer.OrdinalIgnoreCase)
            {
                ["language.saved"] = "✅ زبان ذخیره شد",
                ["auth.register"] = "📝 ثبت نام",
                ["auth.login"] = "🔑 ورود",
                ["auth.chooseAction"] = "یک گزینه را انتخاب کنید:",
                ["auth.continuePrompt"] = "برای ادامه لطفاً ثبت نام کنید یا وارد شوید",
                ["auth.passwordPrompt"] = "🔑 برای ورود رمز عبور خود را وارد کنید:",
                ["registration.enterUsername"] = "✏️ نام کاربری مورد نظر خود را وارد کنید:",
                ["registration.rulesAck"] = "✅ مطالعه کردم",
                ["registration.ready"] = "👍 عالی! همه چیز برای شروع آماده است. به پلتفرم خوش آمدید!",
                ["registration.success"] = "✅ ثبت نام با موفقیت انجام شد! اکنون می توانید از ربات استفاده کنید.",
                ["registration.referral"] = "🎉 کاربر جدید {0} با لینک ارجاع شما ثبت نام کرد!\n\n💰 شما 10% از سود سرمایه گذاری او را دریافت خواهید کرد!\n🔥 برای درآمد بیشتر دوستان بیشتری دعوت کنید!",
                ["registration.rules"] = @"📜 *راهنمای ثبت نام و پرداخت — StarQuantum.AI*

مرحله 1 — اطلاعات ورود خود را ایجاد کنید
• نام کاربری: یک نام یکتا انتخاب کنید که به خاطر بسپارید
• رمز عبور: حداقل 8 کاراکتر شامل حروف، اعداد و نمادها. آن را با کسی به اشتراک نگذارید

مرحله 2 — آدرس کیف پول پرداخت را وارد کنید
• در بخش ""Payment Wallet (USDT - TRC20)"" آدرس کیف پولی را وارد کنید که با آن واریز انجام می دهید
• فقط USDT در شبکه TRC20 پذیرفته می شود. ارسال از شبکه دیگر باعث از دست رفتن دائمی دارایی می شود
• قبل از ارسال، آدرس را با دقت بررسی کنید

مرحله 3 — اتصال واریزها
• همه واریزهای انجام شده از این کیف پول به صورت خودکار به موجودی StarQuantum.AI شما اضافه می شود
• در صورت نیاز به بازگشت وجه، فقط به همان کیف پول بازگردانده می شود

مرحله 4 — تایید
• پس از ثبت نام، به پنل امن StarQuantum.AI دسترسی خواهید داشت
• StarQuantum.AI هرگز عبارت بازیابی یا کلید خصوصی شما را درخواست نمی کند. دارایی شما تحت کنترل خودتان باقی می ماند",
                ["settings.title"] = "⚙️ *منوی تنظیمات*\n\nچه چیزی را می خواهید تنظیم کنید؟",
                ["settings.language"] = "🌐 تغییر زبان",
                ["settings.login"] = "👤 تغییر نام کاربری",
                ["settings.password"] = "🔒 تغییر رمز عبور",
                ["settings.wallet"] = "💰 تغییر آدرس کیف پول",
                ["settings.backMenu"] = "🔙 بازگشت به منوی اصلی",
                ["settings.languageTitle"] = "🌍 *انتخاب زبان*\n\nزبان مورد نظر خود را انتخاب کنید:",
                ["settings.backSettings"] = "↩️ بازگشت به تنظیمات",
                ["settings.askLogin"] = "👤 *تغییر نام کاربری*\n\nنام کاربری جدید خود را وارد کنید:\n\n📝 *شرایط:*\n• 3 تا 20 کاراکتر\n• فقط حروف و اعداد\n• بدون کاراکترهای خاص",
                ["settings.askPassword"] = "🔐 *تغییر رمز عبور*\n\nرمز عبور جدید خود را وارد کنید:\n\n🔒 *توصیه های امنیتی:*\n• حداقل 8 کاراکتر\n• ترکیبی از حروف و اعداد\n• از رمزهای ساده استفاده نکنید",
                ["settings.askWallet"] = "💰 *تغییر آدرس کیف پول*\n\nآدرس جدید کیف پول خود را وارد کنید:\n\n📝 *شرایط:*\n• آدرس معتبر کیف پول رمزارزی\n• مطمئن شوید آدرس صحیح است\n• پیش از ارسال دوباره بررسی کنید",
                ["settings.languageUpdatedSuccess"] = "✅ *زبان با موفقیت به روز شد!*\n\nزبان شما به {0} تغییر کرد.",
                ["settings.languageUpdatedFailed"] = "❌ *به روزرسانی زبان انجام نشد*\n\nدر حال حاضر امکان تغییر زبان وجود ندارد. لطفاً بعداً دوباره تلاش کنید.",
                ["menu.welcome"] = "👋 <b>{0}، خوش آمدید!</b>\n\nمن <b>Shanti</b> هستم، دستیار سرمایه گذاری هوشمند شما.\nامروز چطور می توانم کمک کنم؟",
                ["menu.deposit"] = "واریز",
                ["menu.invest"] = "سرمایه گذاری",
                ["menu.profile"] = "پروفایل من",
                ["menu.withdraw"] = "برداشت",
                ["menu.referral"] = "پاداش های دعوت",
                ["menu.quests"] = "ماموریت ها",
                ["menu.about"] = "درباره StarQuantum.AI",
                ["menu.support"] = "پشتیبانی",
                ["menu.settings"] = "تنظیمات",
                ["maintenance.start"] = "🔧 <b>نگهداری فنی</b>\n\nربات موقتاً در دسترس نیست.\n<b>دلیل:</b> {0}\n\nلطفاً منتظر بمانید!",
                ["maintenance.end"] = "✅ <b>ربات دوباره فعال شد</b>\n\nربات اکنون در دسترس است و به صورت عادی کار می کند!\nاز شکیبایی شما سپاسگزاریم!",
                ["delay.defaultReason"] = "شرایط بازار",
                ["delay.message"] = "<b>📢 اطلاعیه مهم</b>\n\nمعامله گر عزیز،\n\nبه دلیل {0}، پرداخت ها از صرافی همکار ما با 5 روز تأخیر انجام می شود. این وضعیت موقتی است و ما در حال رفع مشکل هستیم.\n\n<b>✅ برای قدردانی از صبوری شما:</b>\nما <b>3 USDT پاداش</b> به حساب شما اضافه کرده ایم! می توانید از این مبلغ برای معامله یا برداشت استفاده کنید.\n\n🙏 از درک و حمایت شما سپاسگزاریم!\n\n<b>🔜 به روزرسانی ها:</b>\nبه محض عادی شدن وضعیت، شما را مطلع خواهیم کرد.\n\n—\nتیم StarQuantum.AI ❤️",
                ["chart.title"] = "💰 موجودی پیش بینی شده",
                ["chart.profit"] = "سود:",
                ["chart.balance"] = "موجودی جدید:",
                ["chart.legend"] = "📈 کندل های سبز مسیر رشد سود شما را نشان می دهند"
            },
            [BotLanguageCodes.Arabic] = new(StringComparer.OrdinalIgnoreCase)
            {
                ["language.saved"] = "✅ تم حفظ اللغة",
                ["auth.register"] = "📝 التسجيل",
                ["auth.login"] = "🔑 تسجيل الدخول",
                ["auth.chooseAction"] = "اختر إجراءً:",
                ["auth.continuePrompt"] = "يرجى التسجيل أو تسجيل الدخول للمتابعة",
                ["auth.passwordPrompt"] = "🔑 أدخل كلمة المرور لتسجيل الدخول:",
                ["registration.enterUsername"] = "✏️ أدخل اسم المستخدم المطلوب:",
                ["registration.rulesAck"] = "✅ لقد قرأت",
                ["registration.ready"] = "👍 رائع! كل شيء جاهز الآن للبدء. مرحباً بك في المنصة!",
                ["registration.success"] = "✅ تم التسجيل بنجاح! يمكنك الآن استخدام البوت.",
                ["registration.referral"] = "🎉 قام المستخدم الجديد {0} بالتسجيل عبر رابط الإحالة الخاص بك!\n\n💰 ستحصل على 10% من أرباح استثماراته!\n🔥 ادعُ المزيد من الأصدقاء لكسب المزيد!",
                ["registration.rules"] = @"📜 *تعليمات التسجيل والدفع — StarQuantum.AI*

الخطوة 1 — أنشئ بيانات تسجيل الدخول
• اسم المستخدم: اختر اسماً فريداً يمكنك تذكره
• كلمة المرور: 8 أحرف على الأقل مع مزيج من الأحرف والأرقام والرموز. لا تشاركها مع أحد

الخطوة 2 — أدخل عنوان محفظة الدفع
• في حقل ""Payment Wallet (USDT - TRC20)"" أدخل عنوان المحفظة التي سترسل منها الأموال
• يتم قبول USDT على شبكة TRC20 فقط. الإرسال من شبكة أخرى يؤدي إلى فقدان دائم للأموال
• تحقق من العنوان جيداً قبل الإرسال

الخطوة 3 — ربط الإيداعات
• كل عملية إيداع من هذه المحفظة ستتم إضافتها تلقائياً إلى رصيدك في StarQuantum.AI
• في حال الحاجة إلى استرداد، سيتم الإرجاع إلى نفس المحفظة فقط

الخطوة 4 — التأكيد
• بعد التسجيل ستحصل على وصول آمن إلى لوحة StarQuantum.AI
• لن يطلب StarQuantum.AI أبداً عبارة الاسترداد أو المفاتيح الخاصة. أموالك تبقى تحت سيطرتك الكاملة",
                ["settings.title"] = "⚙️ *قائمة الإعدادات*\n\nماذا تريد أن تضبط؟",
                ["settings.language"] = "🌐 تغيير اللغة",
                ["settings.login"] = "👤 تغيير اسم المستخدم",
                ["settings.password"] = "🔒 تغيير كلمة المرور",
                ["settings.wallet"] = "💰 تغيير عنوان المحفظة",
                ["settings.backMenu"] = "🔙 العودة إلى القائمة الرئيسية",
                ["settings.languageTitle"] = "🌍 *اختيار اللغة*\n\nاختر لغتك المفضلة:",
                ["settings.backSettings"] = "↩️ العودة إلى الإعدادات",
                ["settings.askLogin"] = "👤 *تغيير اسم المستخدم*\n\nأدخل اسم المستخدم الجديد:\n\n📝 *المتطلبات:*\n• من 3 إلى 20 حرفاً\n• أحرف وأرقام فقط\n• بدون رموز خاصة",
                ["settings.askPassword"] = "🔐 *تغيير كلمة المرور*\n\nأدخل كلمة المرور الجديدة:\n\n🔒 *توصيات الأمان:*\n• 8 أحرف على الأقل\n• مزيج من الأحرف والأرقام\n• تجنب كلمات المرور الشائعة",
                ["settings.askWallet"] = "💰 *تغيير عنوان المحفظة*\n\nأدخل عنوان المحفظة الجديد:\n\n📝 *المتطلبات:*\n• عنوان محفظة عملات رقمية صالح\n• تأكد من صحة العنوان\n• راجعه مرة أخرى قبل الإرسال",
                ["settings.languageUpdatedSuccess"] = "✅ *تم تحديث اللغة بنجاح!*\n\nتم تغيير لغتك إلى {0}.",
                ["settings.languageUpdatedFailed"] = "❌ *فشل تحديث اللغة*\n\nتعذر تغيير اللغة حالياً. حاول مرة أخرى لاحقاً.",
                ["menu.welcome"] = "👋 <b>مرحباً {0}!</b>\n\nأنا <b>Shanti</b>، مساعدك الذكي للاستثمار.\nكيف يمكنني مساعدتك اليوم؟",
                ["menu.deposit"] = "إيداع",
                ["menu.invest"] = "استثمار",
                ["menu.profile"] = "ملفي الشخصي",
                ["menu.withdraw"] = "سحب",
                ["menu.referral"] = "مكافآت الإحالة",
                ["menu.quests"] = "المهام",
                ["menu.about"] = "حول StarQuantum.AI",
                ["menu.support"] = "الدعم والمساعدة",
                ["menu.settings"] = "الإعدادات",
                ["maintenance.start"] = "🔧 <b>صيانة تقنية</b>\n\nالبوت غير متاح مؤقتاً.\n<b>السبب:</b> {0}\n\nيرجى الانتظار!",
                ["maintenance.end"] = "✅ <b>تمت استعادة البوت</b>\n\nالبوت متاح الآن ويعمل بشكل طبيعي!\nشكراً لانتظارك!",
                ["delay.defaultReason"] = "ظروف السوق",
                ["delay.message"] = "<b>📢 تحديث مهم</b>\n\nعزيزي المتداول،\n\nبسبب {0}، يوجد تأخير لمدة 5 أيام في المدفوعات من منصة التداول التي نتعامل معها. هذه حالة مؤقتة ونحن نعمل على حلها.\n\n<b>✅ تقديراً لصبرك:</b>\nلقد أضفنا <b>3 USDT مكافأة</b> إلى حسابك! يمكنك استخدام هذا المبلغ للتداول أو السحب.\n\n🙏 شكراً لتفهمك ودعمك!\n\n<b>🔜 التحديثات:</b>\nسنقوم بإبلاغك فور عودة الوضع إلى طبيعته.\n\n—\nفريق StarQuantum.AI ❤️",
                ["chart.title"] = "💰 الرصيد المتوقع",
                ["chart.profit"] = "الربح:",
                ["chart.balance"] = "الرصيد الجديد:",
                ["chart.legend"] = "📈 الشموع الخضراء تمثل مسار نمو أرباحك"
            },
            [BotLanguageCodes.SimplifiedChinese] = new(StringComparer.OrdinalIgnoreCase)
            {
                ["language.saved"] = "✅ 语言已保存",
                ["auth.register"] = "📝 注册",
                ["auth.login"] = "🔑 登录",
                ["auth.chooseAction"] = "请选择操作：",
                ["auth.continuePrompt"] = "请先注册或登录以继续",
                ["auth.passwordPrompt"] = "🔑 请输入登录密码：",
                ["registration.enterUsername"] = "✏️ 请输入您想使用的用户名：",
                ["registration.rulesAck"] = "✅ 我已阅读",
                ["registration.ready"] = "👍 很好！现在一切都已准备就绪。欢迎来到平台！",
                ["registration.success"] = "✅ 注册成功！您现在可以使用机器人了。",
                ["registration.referral"] = "🎉 新用户 {0} 通过您的邀请链接完成了注册！\n\n💰 您将获得其投资利润的 10%！\n🔥 邀请更多朋友来赚取更多收益！",
                ["registration.rules"] = @"📜 *StarQuantum.AI 注册与支付说明*

步骤 1 — 创建登录信息
• 用户名：请选择一个容易记住的唯一名称
• 密码：至少 8 个字符，包含字母、数字和符号。不要与任何人分享

步骤 2 — 输入支付钱包地址
• 在 ""Payment Wallet (USDT - TRC20)"" 栏位中输入您用于转账的钱包地址
• 仅接受 TRC20 网络的 USDT。从其他网络发送将导致资金永久丢失
• 转账前请再次核对地址

步骤 3 — 资金关联
• 使用该钱包地址完成的所有充值都会自动计入您的 StarQuantum.AI 余额
• 如需退款，也只会退回到同一个钱包地址

步骤 4 — 确认
• 注册后，您将获得对 StarQuantum.AI 面板的安全访问权限
• StarQuantum.AI 绝不会向您索取助记词或私钥。您的资产始终由您自己掌控",
                ["settings.title"] = "⚙️ *设置菜单*\n\n您想配置什么？",
                ["settings.language"] = "🌐 更改语言",
                ["settings.login"] = "👤 更改登录名",
                ["settings.password"] = "🔒 更改密码",
                ["settings.wallet"] = "💰 更改钱包地址",
                ["settings.backMenu"] = "🔙 返回主菜单",
                ["settings.languageTitle"] = "🌍 *语言选择*\n\n请选择您偏好的语言：",
                ["settings.backSettings"] = "↩️ 返回设置",
                ["settings.askLogin"] = "👤 *更改登录名*\n\n请输入新的用户名：\n\n📝 *要求：*\n• 3-20 个字符\n• 仅限字母和数字\n• 不允许特殊字符",
                ["settings.askPassword"] = "🔐 *更改密码*\n\n请输入新密码：\n\n🔒 *安全建议：*\n• 至少 8 个字符\n• 混合字母和数字\n• 避免使用常见密码",
                ["settings.askWallet"] = "💰 *更改钱包地址*\n\n请输入新的钱包地址：\n\n📝 *要求：*\n• 有效的加密货币钱包地址\n• 请确保地址正确\n• 提交前请再次检查",
                ["settings.languageUpdatedSuccess"] = "✅ *语言更新成功！*\n\n您的语言已切换为 {0}。",
                ["settings.languageUpdatedFailed"] = "❌ *语言更新失败*\n\n当前无法更改语言，请稍后重试。",
                ["menu.welcome"] = "👋 <b>欢迎，{0}！</b>\n\n我是 <b>Shanti</b>，您的 AI 投资助手。\n今天我可以如何帮助您？",
                ["menu.deposit"] = "充值",
                ["menu.invest"] = "投资",
                ["menu.profile"] = "我的资料",
                ["menu.withdraw"] = "提现",
                ["menu.referral"] = "邀请奖励",
                ["menu.quests"] = "任务",
                ["menu.about"] = "关于 StarQuantum.AI",
                ["menu.support"] = "支持与帮助",
                ["menu.settings"] = "设置",
                ["maintenance.start"] = "🔧 <b>技术维护</b>\n\n机器人暂时不可用。\n<b>原因：</b>{0}\n\n请稍候！",
                ["maintenance.end"] = "✅ <b>机器人已恢复</b>\n\n机器人现已恢复正常运行！\n感谢您的等待！",
                ["delay.defaultReason"] = "市场情况",
                ["delay.message"] = "<b>📢 重要通知</b>\n\n尊敬的交易者：\n\n由于 {0}，我们合作交易所的付款将延迟 5 天。这是临时情况，我们正在积极处理。\n\n<b>✅ 感谢您的耐心：</b>\n我们已向您的账户添加 <b>3 USDT 奖励</b>！您可以将这笔金额用于交易或提现。\n\n🙏 感谢您的理解与支持！\n\n<b>🔜 后续更新：</b>\n情况恢复正常后，我们会立即通知您。\n\n—\nStarQuantum.AI 团队 ❤️",
                ["chart.title"] = "💰 预计余额",
                ["chart.profit"] = "利润：",
                ["chart.balance"] = "新余额：",
                ["chart.legend"] = "📈 绿色蜡烛线表示您的利润增长轨迹"
            }
        };

    public IReadOnlyList<SupportedBotLanguage> GetSupportedLanguages() => SupportedLanguages;

    public SupportedBotLanguage GetLanguage(string? language)
    {
        var normalizedCode = NormalizeCode(language);
        return SupportedLanguages.First(l => l.Code == normalizedCode);
    }

    public bool TryGetLanguageByCallback(string callbackSuffix, out SupportedBotLanguage language)
    {
        language = SupportedLanguages.FirstOrDefault(
            l => l.CallbackSuffix.Equals(callbackSuffix, StringComparison.OrdinalIgnoreCase));

        return language is not null;
    }

    public string NormalizeCode(string? language)
    {
        if (string.IsNullOrWhiteSpace(language))
        {
            return BotLanguageCodes.English;
        }

        var normalized = language.Trim().ToLowerInvariant();

        return normalized switch
        {
            "en" or "english" => BotLanguageCodes.English,
            "hinglish" or "hi" or "hindi" => BotLanguageCodes.Hinglish,
            "ru" or "russian" or "русский" => BotLanguageCodes.Russian,
            "fa" or "farsi" or "persian" or "فارسی" => BotLanguageCodes.Farsi,
            "ar" or "arabic" or "modern standard arabic" or "العربية" => BotLanguageCodes.Arabic,
            "zh" or "zh-hans" or "chinese" or "simplified chinese" or "简体中文" =>
                BotLanguageCodes.SimplifiedChinese,
            _ => BotLanguageCodes.English
        };
    }

    public string NormalizeDisplayName(string? language)
    {
        return GetLanguage(language).LegacyDisplayName;
    }

    public bool IsEnglish(string? language)
    {
        return NormalizeCode(language) == BotLanguageCodes.English;
    }

    public bool IsRtl(string? language)
    {
        return GetLanguage(language).IsRtl;
    }

    public string GetText(string? language, string key, params object[] args)
    {
        var code = NormalizeCode(language);
        var value = TryGetResource(code, key) ?? TryGetResource(BotLanguageCodes.English, key) ?? key;
        var formatted = args.Length == 0
            ? value
            : string.Format(CultureInfo.InvariantCulture, value, args);

        return FormatText(code, formatted);
    }

    public string FormatText(string? language, string text)
    {
        if (!IsRtl(language) || string.IsNullOrEmpty(text))
        {
            return text;
        }

        return $"{RightToLeftMark}{text}";
    }

    public string GetLanguageSelectionWelcomeText()
    {
        return @"
🎯 <b>WELCOME TO STARQUANTUM.AI TRADING PLATFORM</b> 🎯

🤖 <i>Advanced Algorithmic Investment System</i>

🌟 <b>GETTING STARTED</b>

Thank you for choosing StarQuantum.AI — your gateway to intelligent wealth growth through advanced artificial intelligence.

🌍 <b>LANGUAGE SELECTION</b>

English is the default language. You can switch now or later in Settings.

Available languages:
🇬🇧 <b>English</b>
🇮🇳 <b>Hinglish</b>
🇷🇺 <b>Русский</b>
🇮🇷 <b>فارسی</b>
🇸🇦 <b>العربية</b>
🇨🇳 <b>简体中文</b>

💡 <b>WHY CHOOSE STARQUANTUM.AI?</b>
• AI-Powered Trading Algorithms
• Secure Investment Environment
• Transparent Performance Tracking
• 24/7 Automated Portfolio Management

🔒 <b>SECURITY FEATURES</b>
• Military-Grade Encryption
• Non-Custodial Wallet System
• Regular Security Audits
• Privacy-First Approach

🚀 <b>Ready to begin your investment journey?</b>

Select your language below to continue →";
    }

    private static string? TryGetResource(string languageCode, string key)
    {
        if (!Resources.TryGetValue(languageCode, out var languageResources))
        {
            return null;
        }

        return languageResources.TryGetValue(key, out var value) ? value : null;
    }
}