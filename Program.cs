using System;
using System.Threading.Tasks;
using Serilog;
using Telegram.Bot;

namespace InternetSupportBot
{
    public class Program
    {
        private static readonly string BotToken = "БОТ_АПІ_ТОКЕН";

        static void SetupLogging()
        {
            Log.Logger = new LoggerConfiguration()
                .WriteTo.File("bot.log", rollingInterval: RollingInterval.Day)
                .CreateLogger();
        }

        public static async Task Main()
        {
            SetupLogging();
            var databaseService = new DatabaseService("Data Source=bot.db;");
            var userStateManager = new UserStateManager(databaseService);
            var ticketManager = new TicketManager(databaseService);
            var botHandler = new BotHandler(BotToken, userStateManager, ticketManager);

            botHandler.Start();
            Console.WriteLine("Бот запущено...");
            await Task.Delay(-1);
        }
    }
}