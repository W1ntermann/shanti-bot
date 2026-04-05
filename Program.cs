using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using ShantiBotDi.Db;
using ShantiBotDi.Helpers;
using ShantiBotDi.Services;
using Telegram.Bot;

namespace ShantiBotDi;

public class Program
{
    public static async Task Main(string[] args)
    {
        try
        {
            Console.WriteLine("🚀 Запуск ShantiBotDi...");

            var host = CreateHostBuilder(args).Build();
            await InitializeDatabase(host);
            await RunApplication(host);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Критична помилка запуску: {ex.Message}");
            throw;
        }
    }

    private static IHostBuilder CreateHostBuilder(string[] args)
    {
        return Host.CreateDefaultBuilder(args)
            .ConfigureAppConfiguration((hostingContext, config) =>
            {
                config.AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                      .AddJsonFile($"appsettings.{hostingContext.HostingEnvironment.EnvironmentName}.json", 
                                   optional: true, reloadOnChange: true);
                
                if (hostingContext.HostingEnvironment.IsDevelopment())
                {
                    config.AddUserSecrets<Program>();
                }
                
                config.AddEnvironmentVariables();
            })
            .ConfigureServices((context, services) =>
            {
                ConfigureDatabase(services, context.Configuration);
                ConfigureTelegramBot(services, context.Configuration);
                ConfigureApplicationServices(services);
                ConfigureBackgroundServices(services);
            });
    }

    private static void ConfigureDatabase(IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("DefaultConnection");
        
        if (string.IsNullOrEmpty(connectionString))
        {
            throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");
        }

        services.AddDbContext<BotDbContext>(options =>
        {
            options.UseNpgsql(connectionString);
            
            if (configuration.GetValue<bool>("EnableDetailedErrors"))
            {
                options.EnableDetailedErrors();
            }
            
            if (configuration.GetValue<bool>("EnableSensitiveDataLogging"))
            {
                options.EnableSensitiveDataLogging();
            }
        });

        Console.WriteLine($"✅ База даних налаштована");
    }

    private static void ConfigureTelegramBot(IServiceCollection services, IConfiguration configuration)
    {
        var botToken = configuration["TelegramSettings:Token"];
        
        if (string.IsNullOrEmpty(botToken))
        {
            throw new InvalidOperationException("Telegram bot token not configured.");
        }

        services.Configure<TelegramSettings>(configuration.GetSection("TelegramSettings"));
        
        services.AddSingleton<ITelegramBotClient>(provider =>
        {
            var settings = provider.GetRequiredService<IOptions<TelegramSettings>>().Value;
            return new TelegramBotClient(settings.Token);
        });

        Console.WriteLine($"✅ Telegram Bot налаштовано");
    }

    private static void ConfigureApplicationServices(IServiceCollection services)
    {
        // Core services
        services.AddScoped<IManageService, ManageService>();
        services.AddScoped<IProfileService, ProfileService>();
        services.AddScoped<IOperationService, OperationService>();
        services.AddScoped<IUserStateService, UserStateService>();
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IRegisterService, RegisterService>();
        services.AddScoped<IReferralService, ReferralService>();
        
        // Feature services
        services.AddScoped<SettingsService>();
        services.AddScoped<WithdrawalAdminService>();
        services.AddScoped<IInvestmentAdminService, InvestmentAdminService>();
        services.AddScoped<ITopUserService, TopUserService>();
        services.AddScoped<ILoggerService, LoggerService>();
        services.AddScoped<IMaintenanceService, MaintenanceService>();
        services.AddScoped<ITradingSimulationService, TradingSimulationService>();
        services.AddScoped<IChartRendererService, ChartRendererService>();
        services.AddScoped<IDelayCompensationService, DelayCompensationService>();
        services.AddHostedService<QuestBackgroundService>();
        
        // Quest services
        services.AddScoped<IQuestService, QuestService>();
        services.AddScoped<IQuestManagementService, QuestManagementService>();

        Console.WriteLine($"✅ Зареєстровано {services.Count} сервісів");
    }

    private static void ConfigureBackgroundServices(IServiceCollection services)
    {
        services.AddHostedService<TelegramBotService>();
        // Можна додати інші фонові сервіси тут, наприклад для квестів
        // services.AddHostedService<QuestBackgroundService>();
        
        Console.WriteLine($"✅ Фонові сервіси налаштовані");
    }

    private static async Task InitializeDatabase(IHost host)
    {
        using var scope = host.Services.CreateScope();
        var services = scope.ServiceProvider;

        try
        {
            var dbContext = services.GetRequiredService<BotDbContext>();
            
            Console.WriteLine("🔄 Перевірка підключення до бази даних...");
            
            if (await dbContext.Database.CanConnectAsync())
            {
                Console.WriteLine("✅ Підключення до бази даних успішне");
                
                // Застосування міграцій
                Console.WriteLine("🔄 Застосування міграцій...");
                await dbContext.Database.MigrateAsync();
                Console.WriteLine("✅ Міграції застосовано");
                
                // Ініціалізація квестів
                await InitializeQuests(services);
                
                // Ініціалізація інших даних
                
            }
            else
            {
                Console.WriteLine("❌ Не вдалося підключитися до бази даних");
                throw new InvalidOperationException("Cannot connect to database.");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Помилка ініціалізації бази даних: {ex.Message}");
            throw;
        }
    }

    private static async Task InitializeQuests(IServiceProvider services)
    {
        try
        {
            var questManagementService = services.GetRequiredService<IQuestManagementService>();
            var dbContext = services.GetRequiredService<BotDbContext>();
            
            // Ініціалізація стандартних квестів
            Console.WriteLine("🔄 Ініціалізація квестів...");
            await questManagementService.InitializeDailyQuestsAsync();
            
            // Перевірка
            var questsCount = await dbContext.Quests.CountAsync();
            Console.WriteLine($"✅ Квести ініціалізовано: {questsCount} записів");
            
            // Автоматичне призначення квестів існуючим користувачам
            var usersCount = await dbContext.Users.CountAsync();
            Console.WriteLine($"👥 Користувачів у базі: {usersCount}");
            
            // Тут можна додати автоматичне призначення квестів існуючим користувачам
            if (usersCount > 0 && questsCount > 0)
            {
                Console.WriteLine("ℹ️ Для існуючих користувачів квести будуть призначені при першому відкритті");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"⚠️ Помилка ініціалізації квестів: {ex.Message}");
        }
    }

    

    private static async Task RunApplication(IHost host)
    {
        try
        {
            Console.WriteLine("\n" + new string('=', 50));
            Console.WriteLine("🤖 Shanti AI Trading Platform");
            Console.WriteLine("🚀 Telegram Bot запускається...");
            Console.WriteLine(new string('=', 50) + "\n");
            
            await host.RunAsync();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Помилка під час роботи програми: {ex.Message}");
            throw;
        }
    }
}