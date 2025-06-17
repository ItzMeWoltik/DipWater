using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace InternetSupportBot
{
    public class TicketManager
    {
        private readonly Dictionary<string, (int MessageId, string LastMessage)> _ticketMessageIds = new();
        private readonly Dictionary<string, string> _ticketChatIds = new();
        private readonly DatabaseService _databaseService;
        private readonly long _adminChatId = -4103568498;
        public bool IsOperatorOnline { get; private set; } = false;

        public TicketManager(DatabaseService databaseService)
        {
            _databaseService = databaseService;
        }

        public void ToggleOperatorStatus()
        {
            IsOperatorOnline = !IsOperatorOnline;
        }

        public string GenerateTicketId()
        {
            return Guid.NewGuid().ToString();
        }

        public async Task NotifyAdminAsync(ITelegramBotClient bot, long chatId, UserData data)
        {
            if (data.TicketId == null) return;

            var userInfo = data;
            var identification = !string.IsNullOrEmpty(userInfo.ContractNumber)
                ? $"Номер угоди: {userInfo.ContractNumber}"
                : $"Адреса: {userInfo.Address}";

            var message = $"📩 **Нова заявка (ID: {data.TicketId})**\n" +
                          $"Користувач: {chatId}\n" +
                          $"{identification}\n" +
                          $"Тип проблеми: {data.ProblemType}\n" +
                          $"Деталі: {data.ProblemDetails ?? "Не вказано"}";

            var sentMessage = await bot.SendTextMessageAsync(_adminChatId, message, parseMode: ParseMode.Markdown);
            _ticketMessageIds[data.TicketId] = (sentMessage.MessageId, message);
            _ticketChatIds[data.TicketId] = chatId.ToString();
            _databaseService.LogAction(chatId, $"Повідомлення адміну відправлено: {message}");
        }

        public async Task UpdateAdminMessageAsync(ITelegramBotClient bot, string ticketId, string newMessage)
        {
            if (!_ticketMessageIds.ContainsKey(ticketId)) return;

            var (messageId, lastMessage) = _ticketMessageIds[ticketId];
            if (lastMessage != newMessage)
            {
                await bot.EditMessageTextAsync(_adminChatId, messageId, newMessage, parseMode: ParseMode.Markdown);
                _ticketMessageIds[ticketId] = (messageId, newMessage);
                _databaseService.LogAction(_adminChatId, $"Оновлено повідомлення заявки {ticketId}: {newMessage}");
            }
        }

        public string GetLastMessage(string ticketId)
        {
            return _ticketMessageIds.ContainsKey(ticketId) ? _ticketMessageIds[ticketId].LastMessage : string.Empty;
        }

        public async Task HandleOperatorReplyAsync(ITelegramBotClient bot, Message message)
        {
            var chatId = message.Chat.Id;
            var parts = message.Text?.Split(' ', 3);

            if (parts == null || parts.Length < 3)
            {
                await bot.SendTextMessageAsync(chatId, "Формат команди: /reply <ticketId> <response>");
                _databaseService.LogAction(chatId, "Невірний формат команди /reply");
                return;
            }

            var ticketId = parts[1];
            var response = parts[2];

            if (_ticketChatIds.ContainsKey(ticketId))
            {
                var userChatId = Convert.ToInt64(_ticketChatIds[ticketId]);
                _databaseService.UpdateTicketResponse(ticketId, response);
                var keyboard = new InlineKeyboardMarkup(new[]
                {
                    new[] { InlineKeyboardButton.WithCallbackData("Так", "operator_response_yes") },
                    new[] { InlineKeyboardButton.WithCallbackData("Ні", "operator_response_no") },
                    new[] { InlineKeyboardButton.WithCallbackData("Повернутися в головне меню", "back_to_menu") }
                });
                await bot.SendTextMessageAsync(userChatId, $"Відповідь оператора: {response}", replyMarkup: keyboard);
                await UpdateAdminMessageAsync(bot, ticketId, $"{_ticketMessageIds[ticketId].LastMessage}\nОновлено оператором: {response}");
                _databaseService.LogAction(userChatId, $"Отримано відповідь на заявку {ticketId}: {response}");
            }
            else
            {
                await bot.SendTextMessageAsync(chatId, "Заявка не знайдена.");
                _databaseService.LogAction(chatId, $"Помилка відповіді на заявку {ticketId}: заявка не знайдена");
            }
        }

        public async Task HandleOperatorDirectReplyAsync(ITelegramBotClient bot, Message message)
        {
            var chatId = message.Chat.Id;
            var response = message.Text;
            var repliedMessageText = message.ReplyToMessage?.Text;

            if (string.IsNullOrEmpty(repliedMessageText))
            {
                await bot.SendTextMessageAsync(chatId, "Помилка: немає повідомлення для відповіді.");
                _databaseService.LogAction(chatId, "Помилка: немає повідомлення для відповіді");
                return;
            }

            string ticketId = null;
            foreach (var line in repliedMessageText.Split('\n'))
            {
                if (line.StartsWith("📩 **Нова заявка (ID: "))
                {
                    ticketId = line.Replace("📩 **Нова заявка (ID: ", "").TrimEnd(')').Trim();
                    break;
                }
            }

            if (ticketId == null || !_ticketChatIds.ContainsKey(ticketId))
            {
                await bot.SendTextMessageAsync(chatId, "Помилка: заявка не знайдена.");
                _databaseService.LogAction(chatId, "Помилка: заявка не знайдена для відповіді");
                return;
            }

            var userChatId = Convert.ToInt64(_ticketChatIds[ticketId]);
            _databaseService.UpdateTicketResponse(ticketId, response);
            var keyboard = new InlineKeyboardMarkup(new[]
            {
                new[] { InlineKeyboardButton.WithCallbackData("Так", "operator_response_yes") },
                new[] { InlineKeyboardButton.WithCallbackData("Ні", "operator_response_no") },
                new[] { InlineKeyboardButton.WithCallbackData("Повернутися в головне меню", "back_to_menu") }
            });
            await bot.SendTextMessageAsync(userChatId, $"Відповідь оператора: {response}", replyMarkup: keyboard);
            await UpdateAdminMessageAsync(bot, ticketId, $"{_ticketMessageIds[ticketId].LastMessage}\nОновлено оператором: {response}");
            _databaseService.LogAction(userChatId, $"Отримано відповідь на заявку {ticketId}: {response}");
        }

        public async Task CloseTicketAsync(ITelegramBotClient bot, long chatId, string ticketId)
        {
            if (ticketId != null)
            {
                _databaseService.UpdateTicketStatus(ticketId, "closed");
                _ticketMessageIds.Remove(ticketId);
                _ticketChatIds.Remove(ticketId);
                await bot.SendTextMessageAsync(_adminChatId, $"Заявка {ticketId} закрита користувачем.");
                _databaseService.LogAction(chatId, $"Заявка {ticketId} закрита");
            }
        }
    }
}