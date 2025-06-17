using System;
using System.Threading;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using Serilog;

namespace InternetSupportBot
{
    public class BotHandler
    {
        private readonly TelegramBotClient _bot;
        private readonly UserStateManager _userStateManager;
        private readonly TicketManager _ticketManager;
        private readonly long _adminChatId = -4103568498;

        public BotHandler(string botToken, UserStateManager userStateManager, TicketManager ticketManager)
        {
            _bot = new TelegramBotClient(botToken);
            _userStateManager = userStateManager;
            _ticketManager = ticketManager;
        }

        public void Start()
        {
            _bot.StartReceiving(HandleUpdateAsync, HandleErrorAsync);
        }

        private async Task HandleUpdateAsync(ITelegramBotClient botClient, Update update, CancellationToken cancellationToken)
        {
            if (update.Message != null)
            {
                var message = update.Message;
                var chatId = message.Chat.Id;
                LogAction(chatId, $"Отримано повідомлення: {message.Text}");

                if (chatId == _adminChatId)
                {
                    await HandleAdminMessage(message);
                    return;
                }

                await _userStateManager.HandleUserMessageAsync(_bot, message);
            }
            else if (update.CallbackQuery != null)
            {
                await _userStateManager.HandleCallbackQueryAsync(_bot, update.CallbackQuery, _ticketManager);
            }
        }

        private async Task HandleAdminMessage(Message message)
        {
            if (message.Text == "/online")
            {
                _ticketManager.ToggleOperatorStatus();
                await _bot.SendTextMessageAsync(message.Chat.Id, $"Статус оператора: {(_ticketManager.IsOperatorOnline ? "онлайн" : "офлайн")}");
                LogAction(message.Chat.Id, $"Оператор змінив статус на: {(_ticketManager.IsOperatorOnline ? "онлайн" : "офлайн")}");
            }
            else if (message.ReplyToMessage != null && message.Text?.Trim().Length > 0 && !message.Text.StartsWith("/"))
            {
                await _ticketManager.HandleOperatorDirectReplyAsync(_bot, message);
            }
            else if (message.Text?.StartsWith("/reply") == true)
            {
                await _ticketManager.HandleOperatorReplyAsync(_bot, message);
            }
            else
            {
                await _bot.SendTextMessageAsync(message.Chat.Id, "Використовуйте /online для зміни статусу або надсилайте відповіді напряму.");
                LogAction(message.Chat.Id, "Невідома команда в чаті адмінів");
            }
        }

        private static void LogAction(long chatId, string action)
        {
            Log.Information($"ChatId: {chatId}, Action: {action}, Time: {DateTime.Now}");
        }

        private static Task HandleErrorAsync(ITelegramBotClient botClient, Exception exception, CancellationToken cancellationToken)
        {
            Log.Error(exception, "Помилка в боті");
            return Task.CompletedTask;
        }
    }
}