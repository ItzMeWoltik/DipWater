using System;
using System.Threading;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.ReplyMarkups;
using Microsoft.Data.Sqlite;
using Serilog;
using System.Collections.Generic;

namespace InternetSupportBot
{
    public class Program
    {
        private static TelegramBotClient? Bot;
        private static readonly string ConnectionString = "Data Source=bot.db;";
        private static readonly Dictionary<long, UserState> UserStates = new();
        private static readonly Dictionary<long, UserData> UserData = new();
        private static readonly long AdminChatId = -4103568498;
        private static readonly string BotToken = "7066913623:AAH3Y50dEl8PgQ8T3B3q1wyBIsGjIpWwCL4";
        private static bool IsOperatorOnline = false;
        private static readonly Dictionary<string, (int MessageId, string LastMessage)> TicketMessageIds = new();
        private static readonly Dictionary<string, string> TicketChatIds = new();

        public enum UserState
        {
            Start,
            MainMenu,
            Help,
            CheckBalance,
            TechnicalSupport,
            IdentificationMethod,
            EnterContractNumber,
            EnterAddress,
            ProblemType,
            PaymentIssue,
            ConnectionIssue,
            RouterIssue,
            SpeedIssue,
            OtherIssue,
            ConfirmIssue,
            WaitingOperator,
            OperatorConnected,
            ProblemNotResolved1,
            ProblemNotResolved2
        }

        static void SetupLogging()
        {
            Log.Logger = new LoggerConfiguration()
                .WriteTo.File("bot.log", rollingInterval: RollingInterval.Day)
                .CreateLogger();
        }

        static void SetupDatabase()
        {
            using var connection = new SqliteConnection(ConnectionString);
            connection.Open();

            var command = connection.CreateCommand();
            command.CommandText = @"
                CREATE TABLE IF NOT EXISTS Users (
                    ChatId INTEGER PRIMARY KEY,
                    ContractNumber TEXT,
                    Address TEXT
                )";
            command.ExecuteNonQuery();

            command.CommandText = @"
                CREATE TABLE IF NOT EXISTS Tickets (
                    TicketId TEXT PRIMARY KEY,
                    ChatId INTEGER,
                    ProblemType TEXT,
                    ProblemDetails TEXT,
                    Status TEXT,
                    Response TEXT
                )";
            command.ExecuteNonQuery();

            command.CommandText = @"
                CREATE TABLE IF NOT EXISTS Logs (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    ChatId INTEGER,
                    Action TEXT,
                    Timestamp TEXT
                )";
            command.ExecuteNonQuery();
        }

        static void LogAction(long chatId, string action)
        {
            Log.Information($"ChatId: {chatId}, Action: {action}, Time: {DateTime.Now}");
            using var connection = new SqliteConnection(ConnectionString);
            connection.Open();
            var command = connection.CreateCommand();
            command.CommandText = "INSERT INTO Logs (ChatId, Action, Timestamp) VALUES ($chatId, $action, $timestamp)";
            command.Parameters.AddWithValue("$chatId", chatId);
            command.Parameters.AddWithValue("$action", action);
            command.Parameters.AddWithValue("$timestamp", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            command.ExecuteNonQuery();
        }

        static bool CheckUserExists(long chatId, string? contractNumber = null, string? address = null)
        {
            using var connection = new SqliteConnection(ConnectionString);
            connection.Open();
            var command = connection.CreateCommand();
            if (contractNumber != null)
                command.CommandText = "SELECT COUNT(*) FROM Users WHERE ContractNumber = $contractNumber";
            else
                command.CommandText = "SELECT COUNT(*) FROM Users WHERE Address = $address";
            command.Parameters.AddWithValue(contractNumber != null ? "$contractNumber" : "$address", contractNumber ?? address ?? "");
            var count = Convert.ToInt32(command.ExecuteScalar());
            return count > 0;
        }

        static void SaveUser(long chatId, string? contractNumber, string? address)
        {
            using var connection = new SqliteConnection(ConnectionString);
            connection.Open();
            var command = connection.CreateCommand();
            command.CommandText = "INSERT OR REPLACE INTO Users (ChatId, ContractNumber, Address) VALUES ($chatId, $contractNumber, $address)";
            command.Parameters.AddWithValue("$chatId", chatId);
            command.Parameters.AddWithValue("$contractNumber", contractNumber ?? "");
            command.Parameters.AddWithValue("$address", address ?? "");
            command.ExecuteNonQuery();
        }

        static void SaveTicket(long chatId, string ticketId, string? problemType, string? problemDetails, string status)
        {
            using var connection = new SqliteConnection(ConnectionString);
            connection.Open();
            var command = connection.CreateCommand();
            command.CommandText = "INSERT INTO Tickets (TicketId, ChatId, ProblemType, ProblemDetails, Status) VALUES ($ticketId, $chatId, $problemType, $problemDetails, $status)";
            command.Parameters.AddWithValue("$ticketId", ticketId);
            command.Parameters.AddWithValue("$chatId", chatId);
            command.Parameters.AddWithValue("$problemType", problemType ?? "");
            command.Parameters.AddWithValue("$problemDetails", problemDetails ?? "");
            command.Parameters.AddWithValue("$status", status);
            command.ExecuteNonQuery();
            TicketChatIds[ticketId] = chatId.ToString();
        }

        static void UpdateTicketStatus(string ticketId, string status)
        {
            using var connection = new SqliteConnection(ConnectionString);
            connection.Open();
            var command = connection.CreateCommand();
            command.CommandText = "UPDATE Tickets SET Status = $status WHERE TicketId = $ticketId";
            command.Parameters.AddWithValue("$status", status);
            command.Parameters.AddWithValue("$ticketId", ticketId);
            command.ExecuteNonQuery();
        }

        static void UpdateTicketResponse(string ticketId, string response)
        {
            using var connection = new SqliteConnection(ConnectionString);
            connection.Open();
            var command = connection.CreateCommand();
            command.CommandText = "UPDATE Tickets SET Response = $response, Status = 'answered' WHERE TicketId = $ticketId";
            command.Parameters.AddWithValue("$response", response);
            command.Parameters.AddWithValue("$ticketId", ticketId);
            command.ExecuteNonQuery();
        }

        static string GenerateTicketId()
        {
            return Guid.NewGuid().ToString();
        }

        async static Task NotifyAdmin(long chatId, UserData data)
        {
            if (Bot == null || data.TicketId == null) return;

            var userInfo = UserData[chatId];
            var identification = !string.IsNullOrEmpty(userInfo.ContractNumber)
                ? $"Номер угоди: {userInfo.ContractNumber}"
                : $"Адреса: {userInfo.Address}";

            var message = $"📩 **Нова заявка (ID: {data.TicketId})**\n" +
                          $"Користувач: {chatId}\n" +
                          $"{identification}\n" +
                          $"Тип проблеми: {data.ProblemType}\n" +
                          $"Деталі: {data.ProblemDetails ?? "Не вказано"}";

            var sentMessage = await Bot.SendTextMessageAsync(AdminChatId, message, parseMode: Telegram.Bot.Types.Enums.ParseMode.Markdown);
            TicketMessageIds[data.TicketId] = (sentMessage.MessageId, message);
            LogAction(chatId, $"Повідомлення адміну відправлено: {message}");
        }

        async static Task UpdateAdminMessage(string ticketId, string newMessage)
        {
            if (Bot == null || !TicketMessageIds.ContainsKey(ticketId)) return;

            var (messageId, lastMessage) = TicketMessageIds[ticketId];
            if (lastMessage != newMessage)
            {
                await Bot.EditMessageTextAsync(AdminChatId, messageId, newMessage, parseMode: Telegram.Bot.Types.Enums.ParseMode.Markdown);
                TicketMessageIds[ticketId] = (messageId, newMessage);
                LogAction(AdminChatId, $"Оновлено повідомлення заявки {ticketId}: {newMessage}");
            }
        }

        static async Task HandleUpdateAsync(ITelegramBotClient botClient, Update update, CancellationToken cancellationToken)
        {
            if (Bot == null) return;

            if (update.Message != null)
            {
                var message = update.Message;
                var chatId = message.Chat.Id;
                LogAction(chatId, $"Отримано повідомлення: {message.Text}");

                if (chatId == AdminChatId)
                {
                    if (message.Text == "/online")
                    {
                        IsOperatorOnline = !IsOperatorOnline;
                        await Bot.SendTextMessageAsync(chatId, $"Статус оператора: {(IsOperatorOnline ? "онлайн" : "офлайн")}");
                        LogAction(chatId, $"Оператор змінив статус на: {(IsOperatorOnline ? "онлайн" : "офлайн")}");
                    }
                    else if (message.ReplyToMessage != null && message.ReplyToMessage.Text != null && message.Text?.Trim().Length > 0 && !message.Text.StartsWith("/"))
                    {
                        await HandleOperatorDirectReply(message);
                    }
                    else if (message.Text?.StartsWith("/reply") == true)
                    {
                        await HandleOperatorReply(message);
                    }
                    else
                    {
                        await Bot.SendTextMessageAsync(chatId, "Використовуйте /online для зміни статусу або надсилайте відповіді напряму.");
                        LogAction(chatId, "Невідома команда в чаті адмінів");
                    }
                    return;
                }

                if (!UserStates.ContainsKey(chatId))
                    UserStates[chatId] = UserState.Start;
                if (!UserData.ContainsKey(chatId))
                    UserData[chatId] = new UserData();

                var state = UserStates[chatId];
                var data = UserData[chatId];

                switch (state)
                {
                    case UserState.Start:
                        await HandleStart(message);
                        break;
                    case UserState.IdentificationMethod:
                        await HandleIdentificationMethod(message);
                        break;
                    case UserState.EnterContractNumber:
                        await HandleContractNumber(message);
                        break;
                    case UserState.EnterAddress:
                        await HandleAddress(message);
                        break;
                    case UserState.MainMenu:
                        await HandleMainMenu(message);
                        break;
                    case UserState.Help:
                        await HandleHelp(message);
                        break;
                    case UserState.CheckBalance:
                        await HandleCheckBalance(message);
                        break;
                    case UserState.TechnicalSupport:
                        await HandleTechnicalSupport(message);
                        break;
                    case UserState.ProblemType:
                        await HandleProblemType(message);
                        break;
                    case UserState.PaymentIssue:
                        await HandlePaymentIssue(message);
                        break;
                    case UserState.ConnectionIssue:
                        await HandleConnectionIssue(message);
                        break;
                    case UserState.RouterIssue:
                        await HandleRouterIssue(message);
                        break;
                    case UserState.SpeedIssue:
                        await HandleSpeedIssue(message);
                        break;
                    case UserState.OtherIssue:
                        await HandleOtherIssue(message);
                        break;
                    case UserState.ConfirmIssue:
                        await HandleConfirmIssue(message);
                        break;
                    case UserState.WaitingOperator:
                        await HandleWaitingOperator(message);
                        break;
                    case UserState.OperatorConnected:
                        await HandleOperatorConnected(message);
                        break;
                    case UserState.ProblemNotResolved1:
                        await HandleProblemNotResolved1(message);
                        break;
                    case UserState.ProblemNotResolved2:
                        await HandleProblemNotResolved2(message);
                        break;
                    default:
                        var keyboard = new InlineKeyboardMarkup(new[]
                        {
                            new[] { InlineKeyboardButton.WithCallbackData("Повернутися в головне меню", "back_to_menu") }
                        });
                        await Bot.SendTextMessageAsync(chatId, "Невідомий запит. Оберіть дію нижче:", replyMarkup: keyboard);
                        LogAction(chatId, "Невідомий запит");
                        break;
                }
            }
            else if (update.CallbackQuery != null)
            {
                await HandleCallbackQuery(update.CallbackQuery);
            }
        }

        static async Task HandleOperatorReply(Message message)
        {
            if (Bot == null) return;
            var chatId = message.Chat.Id;
            var parts = message.Text?.Split(' ', 3);

            if (parts == null || parts.Length < 3)
            {
                await Bot.SendTextMessageAsync(chatId, "Формат команди: /reply <ticketId> <response>");
                LogAction(chatId, "Невірний формат команди /reply");
                return;
            }

            var ticketId = parts[1];
            var response = parts[2];

            if (TicketChatIds.ContainsKey(ticketId))
            {
                var userChatId = Convert.ToInt64(TicketChatIds[ticketId]);
                UpdateTicketResponse(ticketId, response);
                var keyboard = new InlineKeyboardMarkup(new[]
                {
                    new[] { InlineKeyboardButton.WithCallbackData("Так", "operator_response_yes") },
                    new[] { InlineKeyboardButton.WithCallbackData("Ні", "operator_response_no") },
                    new[] { InlineKeyboardButton.WithCallbackData("Повернутися в головне меню", "back_to_menu") }
                });
                await Bot.SendTextMessageAsync(userChatId, $"Відповідь оператора: {response}", replyMarkup: keyboard);
                await UpdateAdminMessage(ticketId, $"{TicketMessageIds[ticketId].LastMessage}\nОновлено оператором: {response}");
                LogAction(userChatId, $"Отримано відповідь на заявку {ticketId}: {response}");
            }
            else
            {
                await Bot.SendTextMessageAsync(chatId, "Заявка не знайдена.");
                LogAction(chatId, $"Помилка відповіді на заявку {ticketId}: заявка не знайдена");
            }
        }

        static async Task HandleOperatorDirectReply(Message message)
        {
            if (Bot == null) return;
            var chatId = message.Chat.Id;
            var response = message.Text;
            var repliedMessageText = message.ReplyToMessage?.Text;

            if (string.IsNullOrEmpty(repliedMessageText))
            {
                await Bot.SendTextMessageAsync(chatId, "Помилка: немає повідомлення для відповіді.");
                LogAction(chatId, "Помилка: немає повідомлення для відповіді");
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

            if (ticketId == null || !TicketChatIds.ContainsKey(ticketId))
            {
                await Bot.SendTextMessageAsync(chatId, "Помилка: заявка не знайдена.");
                LogAction(chatId, "Помилка: заявка не знайдена для відповіді");
                return;
            }

            var userChatId = Convert.ToInt64(TicketChatIds[ticketId]);
            UpdateTicketResponse(ticketId, response);
            var keyboard = new InlineKeyboardMarkup(new[]
            {
                new[] { InlineKeyboardButton.WithCallbackData("Так", "operator_response_yes") },
                new[] { InlineKeyboardButton.WithCallbackData("Ні", "operator_response_no") },
                new[] { InlineKeyboardButton.WithCallbackData("Повернутися в головне меню", "back_to_menu") }
            });
            await Bot.SendTextMessageAsync(userChatId, $"Відповідь оператора: {response}", replyMarkup: keyboard);
            await UpdateAdminMessage(ticketId, $"{TicketMessageIds[ticketId].LastMessage}\nОновлено оператором: {response}");
            LogAction(userChatId, $"Отримано відповідь на заявку {ticketId}: {response}");
        }

        static async Task HandleStart(Message message)
        {
            if (Bot == null) return;
            var chatId = message.Chat.Id;
            UserStates[chatId] = UserState.IdentificationMethod;
            var keyboard = new InlineKeyboardMarkup(new[]
            {
                new[] { InlineKeyboardButton.WithCallbackData("За номером угоди", "by_contract") },
                new[] { InlineKeyboardButton.WithCallbackData("За адресою", "by_address") }
            });
            await Bot.SendTextMessageAsync(chatId, "Вітаємо! Будь ласка, оберіть спосіб ідентифікації:", replyMarkup: keyboard);
            LogAction(chatId, "Запит на ідентифікацію");
        }

        static async Task HandleIdentificationMethod(Message message)
        {
            if (Bot == null) return;
            var chatId = message.Chat.Id;
            var keyboard = new InlineKeyboardMarkup(new[]
            {
                new[] { InlineKeyboardButton.WithCallbackData("Повернутися в головне меню", "back_to_menu") }
            });
            await Bot.SendTextMessageAsync(chatId, "Оберіть спосіб ідентифікації за допомогою кнопок вище.", replyMarkup: keyboard);
            LogAction(chatId, "Невірний вибір методу ідентифікації");
        }

        static async Task HandleContractNumber(Message message)
        {
            if (Bot == null) return;
            var chatId = message.Chat.Id;
            var contractNumber = message.Text;
            if (CheckUserExists(chatId, contractNumber: contractNumber))
            {
                UserData[chatId].ContractNumber = contractNumber;
                UserData[chatId].IsIdentified = true;
                UserStates[chatId] = UserState.MainMenu;
                await ShowMainMenu(chatId);
                LogAction(chatId, $"Ідентифікація успішна за номером угоди: {contractNumber}");
            }
            else
            {
                SaveUser(chatId, contractNumber, "");
                UserData[chatId].ContractNumber = contractNumber;
                UserData[chatId].IsIdentified = true;
                UserStates[chatId] = UserState.MainMenu;
                await ShowMainMenu(chatId);
                LogAction(chatId, $"Новий користувач зареєстрований за номером угоди: {contractNumber}");
            }
        }

        static async Task HandleAddress(Message message)
        {
            if (Bot == null) return;
            var chatId = message.Chat.Id;
            var address = message.Text;
            if (CheckUserExists(chatId, address: address))
            {
                UserData[chatId].Address = address;
                UserData[chatId].IsIdentified = true;
                UserStates[chatId] = UserState.MainMenu;
                await ShowMainMenu(chatId);
                LogAction(chatId, $"Ідентифікація успішна за адресою: {address}");
            }
            else
            {
                SaveUser(chatId, "", address);
                UserData[chatId].Address = address;
                UserData[chatId].IsIdentified = true;
                UserStates[chatId] = UserState.MainMenu;
                await ShowMainMenu(chatId);
                LogAction(chatId, $"Новий користувач зареєстрований за адресою: {address}");
            }
        }

        static async Task ShowMainMenu(long chatId)
        {
            if (Bot == null) return;
            var keyboard = new InlineKeyboardMarkup(new[]
            {
                new[] { InlineKeyboardButton.WithCallbackData("Довідка", "help") },
                new[] { InlineKeyboardButton.WithCallbackData("Перевірити баланс", "check_balance") },
                new[] { InlineKeyboardButton.WithCallbackData("Технічна підтримка", "technical_support") }
            });
            await Bot.SendTextMessageAsync(chatId, "Оберіть потрібну дію:", replyMarkup: keyboard);
            LogAction(chatId, "Показане головне меню");
        }

        static async Task HandleMainMenu(Message message)
        {
            if (Bot == null) return;
            var chatId = message.Chat.Id;
            var keyboard = new InlineKeyboardMarkup(new[]
            {
                new[] { InlineKeyboardButton.WithCallbackData("Повернутися в головне меню", "back_to_menu") }
            });
            await Bot.SendTextMessageAsync(chatId, "Оберіть дію за допомогою кнопок вище.", replyMarkup: keyboard);
            LogAction(chatId, "Невірний вибір у головному меню");
        }

        static async Task HandleHelp(Message message)
        {
            if (Bot == null) return;
            var chatId = message.Chat.Id;
            var helpText = "📚 **Довідка**\n\n" +
                           "- **Перевірити баланс**: Дізнайтесь стан вашого рахунку.\n" +
                           "- **Технічна підтримка**: Отримайте допомогу з проблемами інтернету чи роутера.\n" +
                           "Оберіть дію у головному меню.";
            var keyboard = new InlineKeyboardMarkup(new[]
            {
                new[] { InlineKeyboardButton.WithCallbackData("Повернутися в головне меню", "back_to_menu") }
            });
            await Bot.SendTextMessageAsync(chatId, helpText, parseMode: Telegram.Bot.Types.Enums.ParseMode.Markdown, replyMarkup: keyboard);
            UserStates[chatId] = UserState.MainMenu;
            LogAction(chatId, "Показана довідка");
        }

        static async Task HandleCheckBalance(Message message)
        {
            if (Bot == null) return;
            var chatId = message.Chat.Id;
            var balance = new Random().Next(0, 1000);
            var keyboard = new InlineKeyboardMarkup(new[]
            {
                new[] { InlineKeyboardButton.WithCallbackData("Повернутися в головне меню", "back_to_menu") }
            });
            await Bot.SendTextMessageAsync(chatId, $"Ваш баланс: {balance} грн.", replyMarkup: keyboard);
            UserStates[chatId] = UserState.MainMenu;
            LogAction(chatId, $"Перевірка балансу: {balance} грн");
        }

        static async Task HandleTechnicalSupport(Message message)
        {
            if (Bot == null) return;
            var chatId = message.Chat.Id;
            var keyboard = new InlineKeyboardMarkup(new[]
            {
                new[] { InlineKeyboardButton.WithCallbackData("Проблема з оплатою", "payment_issue") },
                new[] { InlineKeyboardButton.WithCallbackData("Проблема зі зв’язком", "connection_issue") },
                new[] { InlineKeyboardButton.WithCallbackData("Проблема з роутером", "router_issue") },
                new[] { InlineKeyboardButton.WithCallbackData("Низька швидкість", "speed_issue") },
                new[] { InlineKeyboardButton.WithCallbackData("Інше", "other_issue") },
                new[] { InlineKeyboardButton.WithCallbackData("Повернутися в головне меню", "back_to_menu") }
            });
            await Bot.SendTextMessageAsync(chatId, "Оберіть тип проблеми:", replyMarkup: keyboard);
            UserStates[chatId] = UserState.ProblemType;
            LogAction(chatId, "Запит на вибір типу проблеми");
        }

        static async Task HandleProblemType(Message message)
        {
            if (Bot == null) return;
            var chatId = message.Chat.Id;
            var keyboard = new InlineKeyboardMarkup(new[]
            {
                new[] { InlineKeyboardButton.WithCallbackData("Повернутися в головне меню", "back_to_menu") }
            });
            await Bot.SendTextMessageAsync(chatId, "Оберіть тип проблеми за допомогою кнопок вище.", replyMarkup: keyboard);
            LogAction(chatId, "Невірний вибір типу проблеми");
        }

        static async Task HandlePaymentIssue(Message message)
        {
            if (Bot == null) return;
            var chatId = message.Chat.Id;
            var keyboard = new InlineKeyboardMarkup(new[]
            {
                new[] { InlineKeyboardButton.WithCallbackData("Перевірити статус платежу", "check_payment") }
            });
            var paymentMessage = await Bot.SendTextMessageAsync(chatId, "Проблема з оплатою. Спробуйте наступне:", replyMarkup: keyboard);
            UserData[chatId].MessageIdToEdit = paymentMessage.MessageId;
            UserStates[chatId] = UserState.ProblemNotResolved1;
            LogAction(chatId, "Запит на вирішення проблеми з оплатою");
        }

        static async Task HandleConnectionIssue(Message message)
        {
            if (Bot == null) return;
            var chatId = message.Chat.Id;
            var keyboard = new InlineKeyboardMarkup(new[]
            {
                new[] { InlineKeyboardButton.WithCallbackData("Перевірити кабель", "check_cable") }
            });
            var connectionMessage = await Bot.SendTextMessageAsync(chatId, "Проблема зі зв’язком. Спробуйте наступне:", replyMarkup: keyboard);
            UserData[chatId].MessageIdToEdit = connectionMessage.MessageId;
            UserStates[chatId] = UserState.ProblemNotResolved1;
            LogAction(chatId, "Запит на вирішення проблеми зі зв’язком");
        }

        static async Task HandleRouterIssue(Message message)
        {
            if (Bot == null) return;
            var chatId = message.Chat.Id;
            var keyboard = new InlineKeyboardMarkup(new[]
            {
                new[] { InlineKeyboardButton.WithCallbackData("Перевірити індикатори", "check_leds") }
            });
            var routerMessage = await Bot.SendTextMessageAsync(chatId, "Проблема з роутером. Спробуйте наступне:", replyMarkup: keyboard);
            UserData[chatId].MessageIdToEdit = routerMessage.MessageId;
            UserStates[chatId] = UserState.ProblemNotResolved1;
            LogAction(chatId, "Запит на вирішення проблеми з роутером");
        }

        static async Task HandleSpeedIssue(Message message)
        {
            if (Bot == null) return;
            var chatId = message.Chat.Id;
            var keyboard = new InlineKeyboardMarkup(new[]
            {
                new[] { InlineKeyboardButton.WithCallbackData("Перевірити швидкість", "check_speed") }
            });
            var speedMessage = await Bot.SendTextMessageAsync(chatId, "Низька швидкість інтернету. Спробуйте наступне:", replyMarkup: keyboard);
            UserData[chatId].MessageIdToEdit = speedMessage.MessageId;
            UserStates[chatId] = UserState.ProblemNotResolved1;
            LogAction(chatId, "Запит на вирішення проблеми з низькою швидкістю");
        }

        static async Task HandleOtherIssue(Message message)
        {
            if (Bot == null) return;
            var chatId = message.Chat.Id;
            UserData[chatId].ProblemDetails = message.Text;
            var keyboard = new InlineKeyboardMarkup(new[]
            {
                new[] { InlineKeyboardButton.WithCallbackData("Підтвердити", "confirm_issue") },
                new[] { InlineKeyboardButton.WithCallbackData("Скасувати", "cancel_issue") },
                new[] { InlineKeyboardButton.WithCallbackData("Повернутися в головне меню", "back_to_menu") }
            });
            await Bot.SendTextMessageAsync(chatId, $"Ви описали проблему: {UserData[chatId].ProblemDetails}. Підтвердити?", replyMarkup: keyboard);
            UserStates[chatId] = UserState.ConfirmIssue;
            LogAction(chatId, $"Опис проблеми: {UserData[chatId].ProblemDetails}");
        }

        static async Task HandleConfirmIssue(Message message)
        {
            if (Bot == null) return;
            var chatId = message.Chat.Id;
            UserData[chatId].ProblemDetails = message.Text;
            var keyboard = new InlineKeyboardMarkup(new[]
            {
                new[] { InlineKeyboardButton.WithCallbackData("Підтвердити", "confirm_issue") },
                new[] { InlineKeyboardButton.WithCallbackData("Скасувати", "cancel_issue") },
                new[] { InlineKeyboardButton.WithCallbackData("Повернутися в головне меню", "back_to_menu") }
            });
            await Bot.SendTextMessageAsync(chatId, $"Ви описали проблему: {UserData[chatId].ProblemDetails}. Підтвердити?", replyMarkup: keyboard);
            LogAction(chatId, $"Опис проблеми: {UserData[chatId].ProblemDetails}");
        }

        static async Task HandleWaitingOperator(Message message)
        {
            if (Bot == null) return;
            var chatId = message.Chat.Id;
            var keyboard = new InlineKeyboardMarkup(new[]
            {
                new[] { InlineKeyboardButton.WithCallbackData("Повернутися в головне меню", "back_to_menu") }
            });
            await Bot.SendTextMessageAsync(chatId, "Ви в черзі на з’єднання з оператором. Очікуйте, будь ласка.", replyMarkup: keyboard);
            LogAction(chatId, "Користувач у черзі на оператора");
            await Task.Delay(5000);
            UserStates[chatId] = UserState.OperatorConnected;
            var operatorKeyboard = new InlineKeyboardMarkup(new[]
            {
                new[] { InlineKeyboardButton.WithCallbackData("Закрити заявку", "close_ticket") },
                new[] { InlineKeyboardButton.WithCallbackData("Повернутися в головне меню", "back_to_menu") }
            });
            await Bot.SendTextMessageAsync(chatId, "Оператор підключений. Опишіть вашу проблему оператору:", replyMarkup: operatorKeyboard);
            LogAction(chatId, "Оператор підключений");
        }

        static async Task HandleOperatorConnected(Message message)
        {
            if (Bot == null || UserData[message.Chat.Id].TicketId == null) return;
            var chatId = message.Chat.Id;
            var ticketId = UserData[chatId].TicketId;
            
            var userInfo = UserData[chatId];
            var identification = !string.IsNullOrEmpty(userInfo.ContractNumber)
                ? $"Номер угоди: {userInfo.ContractNumber}"
                : $"Адреса: {userInfo.Address}";
            
            var updatedMessage = $"{TicketMessageIds[ticketId].LastMessage}\nДодаткове повідомлення: {message.Text}";
            
            await UpdateAdminMessage(ticketId, updatedMessage);
            
            var keyboard = new InlineKeyboardMarkup(new[]
            {
                new[] { InlineKeyboardButton.WithCallbackData("Закрити заявку", "close_ticket") },
                new[] { InlineKeyboardButton.WithCallbackData("Повернутися в головне меню", "back_to_menu") }
            });
            await Bot.SendTextMessageAsync(chatId, "Ваше повідомлення передано оператору. Очікуйте відповіді.", replyMarkup: keyboard);
            LogAction(chatId, $"Повідомлення оператору: {message.Text}");
        }

        static async Task HandleProblemNotResolved1(Message message)
        {
            if (Bot == null) return;
            var chatId = message.Chat.Id;
            var keyboard = new InlineKeyboardMarkup(new[]
            {
                new[] { InlineKeyboardButton.WithCallbackData("Так", "problem_resolved") },
                new[] { InlineKeyboardButton.WithCallbackData("Ні", "problem_not_resolved_1") },
                new[] { InlineKeyboardButton.WithCallbackData("Повернутися в головне меню", "back_to_menu") }
            });
            await Bot.SendTextMessageAsync(chatId, "Чи допомогло це рішення?", replyMarkup: keyboard);
            LogAction(chatId, "Запит на статус вирішення проблеми (1-й етап)");
        }

        static async Task HandleProblemNotResolved2(Message message)
        {
            if (Bot == null) return;
            var chatId = message.Chat.Id;
            var keyboard = new InlineKeyboardMarkup(new[]
            {
                new[] { InlineKeyboardButton.WithCallbackData("Так", "problem_resolved") },
                new[] { InlineKeyboardButton.WithCallbackData("Ні", "to_operator") },
                new[] { InlineKeyboardButton.WithCallbackData("Повернутися в головне меню", "back_to_menu") }
            });
            await Bot.SendTextMessageAsync(chatId, "Чи допомогло це рішення?", replyMarkup: keyboard);
            LogAction(chatId, "Запит на статус вирішення проблеми (2-й етап)");
        }

        static async Task HandleCallbackQuery(CallbackQuery callback)
        {
            if (Bot == null || callback.Message == null) return;
            var chatId = callback.Message.Chat.Id;
            var data = callback.Data;
            LogAction(chatId, $"Отримано callback: {data}");

            if (data == null) return;

            switch (data)
            {
                case "back_to_menu":
                    UserStates[chatId] = UserState.MainMenu;
                    UserData[chatId].AttemptedSolutions = new HashSet<string>();
                    UserData[chatId].MessageIdToEdit = null;
                    await ShowMainMenu(chatId);
                    LogAction(chatId, "Повернення в головне меню");
                    break;
                case "by_contract":
                    UserStates[chatId] = UserState.EnterContractNumber;
                    var contractKeyboard = new InlineKeyboardMarkup(new[]
                    {
                        new[] { InlineKeyboardButton.WithCallbackData("Повернутися в головне меню", "back_to_menu") }
                    });
                    await Bot.SendTextMessageAsync(chatId, "Введіть номер угоди:", replyMarkup: contractKeyboard);
                    LogAction(chatId, "Обрано ідентифікацію за номером угоди");
                    break;
                case "by_address":
                    UserStates[chatId] = UserState.EnterAddress;
                    var addressKeyboard = new InlineKeyboardMarkup(new[]
                    {
                        new[] { InlineKeyboardButton.WithCallbackData("Повернутися в головне меню", "back_to_menu") }
                    });
                    await Bot.SendTextMessageAsync(chatId, "Введіть адресу (наприклад, вул. Центральна, 10, кв. 5):", replyMarkup: addressKeyboard);
                    LogAction(chatId, "Обрано ідентифікацію за адресою");
                    break;
                case "help":
                    var helpText = "📚 **Довідка**\n\n" +
                                   "- **Перевірити баланс**: Дізнайтесь стан вашого рахунку.\n" +
                                   "- **Технічна підтримка**: Отримайте допомогу з проблемами інтернету чи роутера.\n" +
                                   "Оберіть дію у головному меню.";
                    var helpKeyboard = new InlineKeyboardMarkup(new[]
                    {
                        new[] { InlineKeyboardButton.WithCallbackData("Повернутися в головне меню", "back_to_menu") }
                    });
                    await Bot.SendTextMessageAsync(chatId, helpText, parseMode: Telegram.Bot.Types.Enums.ParseMode.Markdown, replyMarkup: helpKeyboard);
                    UserStates[chatId] = UserState.MainMenu;
                    LogAction(chatId, "Показана довідка");
                    break;
                case "check_balance":
                    var balance = new Random().Next(0, 1000);
                    var balanceKeyboard = new InlineKeyboardMarkup(new[]
                    {
                        new[] { InlineKeyboardButton.WithCallbackData("Повернутися в головне меню", "back_to_menu") }
                    });
                    await Bot.SendTextMessageAsync(chatId, $"Ваш баланс: {balance} грн.", replyMarkup: balanceKeyboard);
                    UserStates[chatId] = UserState.MainMenu;
                    LogAction(chatId, $"Перевірка балансу: {balance} грн");
                    break;
                case "technical_support":
                    var techKeyboard = new InlineKeyboardMarkup(new[]
                    {
                        new[] { InlineKeyboardButton.WithCallbackData("Проблема з оплатою", "payment_issue") },
                        new[] { InlineKeyboardButton.WithCallbackData("Проблема зі зв’язком", "connection_issue") },
                        new[] { InlineKeyboardButton.WithCallbackData("Проблема з роутером", "router_issue") },
                        new[] { InlineKeyboardButton.WithCallbackData("Низька швидкість", "speed_issue") },
                        new[] { InlineKeyboardButton.WithCallbackData("Інше", "other_issue") },
                        new[] { InlineKeyboardButton.WithCallbackData("Повернутися в головне меню", "back_to_menu") }
                    });
                    await Bot.SendTextMessageAsync(chatId, "Оберіть тип проблеми:", replyMarkup: techKeyboard);
                    UserStates[chatId] = UserState.ProblemType;
                    UserData[chatId].AttemptedSolutions = new HashSet<string>();
                    LogAction(chatId, "Запит на вибір типу проблеми");
                    break;
                case "payment_issue":
                    UserData[chatId].ProblemType = "Проблема з оплатою";
                    UserData[chatId].AttemptedSolutions = new HashSet<string>();
                    var paymentKeyboard = new InlineKeyboardMarkup(new[]
                    {
                        new[] { InlineKeyboardButton.WithCallbackData("Перевірити статус платежу", "check_payment") }
                    });
                    var paymentMessage = await Bot.SendTextMessageAsync(chatId, "Проблема з оплатою. Спробуйте наступне:", replyMarkup: paymentKeyboard);
                    UserData[chatId].MessageIdToEdit = paymentMessage.MessageId;
                    UserStates[chatId] = UserState.ProblemNotResolved1;
                    LogAction(chatId, "Запит на вирішення проблеми з оплатою");
                    break;
                case "connection_issue":
                    UserData[chatId].ProblemType = "Проблема зі зв’язком";
                    UserData[chatId].AttemptedSolutions = new HashSet<string>();
                    var connectionKeyboard = new InlineKeyboardMarkup(new[]
                    {
                        new[] { InlineKeyboardButton.WithCallbackData("Перевірити кабель", "check_cable") }
                    });
                    var connectionMessage = await Bot.SendTextMessageAsync(chatId, "Проблема зі зв’язком. Спробуйте наступне:", replyMarkup: connectionKeyboard);
                    UserData[chatId].MessageIdToEdit = connectionMessage.MessageId;
                    UserStates[chatId] = UserState.ProblemNotResolved1;
                    LogAction(chatId, "Запит на вирішення проблеми зі зв’язком");
                    break;
                case "router_issue":
                    UserData[chatId].ProblemType = "Проблема з роутером";
                    UserData[chatId].AttemptedSolutions = new HashSet<string>();
                    var routerKeyboard = new InlineKeyboardMarkup(new[]
                    {
                        new[] { InlineKeyboardButton.WithCallbackData("Перевірити індикатори", "check_leds") }
                    });
                    var routerMessage = await Bot.SendTextMessageAsync(chatId, "Проблема з роутером. Спробуйте наступне:", replyMarkup: routerKeyboard);
                    UserData[chatId].MessageIdToEdit = routerMessage.MessageId;
                    UserStates[chatId] = UserState.ProblemNotResolved1;
                    LogAction(chatId, "Запит на вирішення проблеми з роутером");
                    break;
                case "speed_issue":
                    UserData[chatId].ProblemType = "Низька швидкість";
                    UserData[chatId].AttemptedSolutions = new HashSet<string>();
                    var speedKeyboard = new InlineKeyboardMarkup(new[]
                    {
                        new[] { InlineKeyboardButton.WithCallbackData("Перевірити швидкість", "check_speed") }
                    });
                    var speedMessage = await Bot.SendTextMessageAsync(chatId, "Низька швидкість інтернету. Спробуйте наступне:", replyMarkup: speedKeyboard);
                    UserData[chatId].MessageIdToEdit = speedMessage.MessageId;
                    UserStates[chatId] = UserState.ProblemNotResolved1;
                    LogAction(chatId, "Запит на вирішення проблеми з низькою швидкістю");
                    break;
                case "other_issue":
                    UserData[chatId].ProblemType = "Інше";
                    UserData[chatId].AttemptedSolutions = new HashSet<string>();
                    var otherKeyboard = new InlineKeyboardMarkup(new[]
                    {
                        new[] { InlineKeyboardButton.WithCallbackData("Повернутися в головне меню", "back_to_menu") }
                    });
                    await Bot.SendTextMessageAsync(chatId, "Опишіть вашу проблему:", replyMarkup: otherKeyboard);
                    UserStates[chatId] = UserState.OtherIssue;
                    LogAction(chatId, "Запит на опис іншої проблеми");
                    break;
                case "check_payment":
                    UserData[chatId].AttemptedSolutions.Add("check_payment");
                    await Bot.EditMessageTextAsync(chatId, UserData[chatId].MessageIdToEdit.Value, "Перевірте статус платежу в особистому кабінеті або повторіть оплату.", replyMarkup: new InlineKeyboardMarkup(new[]
                    {
                        new[] { InlineKeyboardButton.WithCallbackData("Так", "problem_resolved") },
                        new[] { InlineKeyboardButton.WithCallbackData("Ні", "problem_not_resolved_1") },
                        new[] { InlineKeyboardButton.WithCallbackData("Повернутися в головне меню", "back_to_menu") }
                    }));
                    LogAction(chatId, "Перевірка статусу платежу (1-й етап)");
                    break;
                case "pay_now":
                    UserData[chatId].AttemptedSolutions.Add("pay_now");
                    await Bot.EditMessageTextAsync(chatId, UserData[chatId].MessageIdToEdit.Value, "Перейдіть до оплати за посиланням: [Оплатити](https://example.com/pay).", replyMarkup: new InlineKeyboardMarkup(new[]
                    {
                        new[] { InlineKeyboardButton.WithCallbackData("Так", "problem_resolved") },
                        new[] { InlineKeyboardButton.WithCallbackData("Ні", "problem_not_resolved_1") },
                        new[] { InlineKeyboardButton.WithCallbackData("Повернутися в головне меню", "back_to_menu") }
                    }));
                    LogAction(chatId, "Запит на оплату (1-й етап)");
                    break;
                case "check_cable":
                    UserData[chatId].AttemptedSolutions.Add("check_cable");
                    await Bot.EditMessageTextAsync(chatId, UserData[chatId].MessageIdToEdit.Value, "Перевірте, чи кабель підключений до роутера та комп’ютера.", replyMarkup: new InlineKeyboardMarkup(new[]
                    {
                        new[] { InlineKeyboardButton.WithCallbackData("Так", "problem_resolved") },
                        new[] { InlineKeyboardButton.WithCallbackData("Ні", "problem_not_resolved_1") },
                        new[] { InlineKeyboardButton.WithCallbackData("Повернутися в головне меню", "back_to_menu") }
                    }));
                    LogAction(chatId, "Перевірка кабелю (1-й етап)");
                    break;
                case "restart_router":
                    UserData[chatId].AttemptedSolutions.Add("restart_router");
                    await Bot.EditMessageTextAsync(chatId, UserData[chatId].MessageIdToEdit.Value, "Вимкніть роутер на 10 секунд і увімкніть знову.", replyMarkup: new InlineKeyboardMarkup(new[]
                    {
                        new[] { InlineKeyboardButton.WithCallbackData("Так", "problem_resolved") },
                        new[] { InlineKeyboardButton.WithCallbackData("Ні", "problem_not_resolved_1") },
                        new[] { InlineKeyboardButton.WithCallbackData("Повернутися в головне меню", "back_to_menu") }
                    }));
                    LogAction(chatId, "Перезапуск роутера (1-й етап)");
                    break;
                case "check_leds":
                    UserData[chatId].AttemptedSolutions.Add("check_leds");
                    await Bot.EditMessageTextAsync(chatId, UserData[chatId].MessageIdToEdit.Value, "Перевірте, чи горять зелені індикатори на роутері.", replyMarkup: new InlineKeyboardMarkup(new[]
                    {
                        new[] { InlineKeyboardButton.WithCallbackData("Так", "problem_resolved") },
                        new[] { InlineKeyboardButton.WithCallbackData("Ні", "problem_not_resolved_1") },
                        new[] { InlineKeyboardButton.WithCallbackData("Повернутися в головне меню", "back_to_menu") }
                    }));
                    LogAction(chatId, "Перевірка індикаторів роутера (1-й етап)");
                    break;
                case "reset_router":
                    UserData[chatId].AttemptedSolutions.Add("reset_router");
                    await Bot.EditMessageTextAsync(chatId, UserData[chatId].MessageIdToEdit.Value, "Натисніть кнопку Reset на роутері на 5 секунд.", replyMarkup: new InlineKeyboardMarkup(new[]
                    {
                        new[] { InlineKeyboardButton.WithCallbackData("Так", "problem_resolved") },
                        new[] { InlineKeyboardButton.WithCallbackData("Ні", "problem_not_resolved_1") },
                        new[] { InlineKeyboardButton.WithCallbackData("Повернутися в головне меню", "back_to_menu") }
                    }));
                    LogAction(chatId, "Скидання налаштувань роутера (1-й етап)");
                    break;
                case "check_speed":
                    UserData[chatId].AttemptedSolutions.Add("check_speed");
                    await Bot.EditMessageTextAsync(chatId, UserData[chatId].MessageIdToEdit.Value, "Перевірте швидкість на сайті speedtest.net.", replyMarkup: new InlineKeyboardMarkup(new[]
                    {
                        new[] { InlineKeyboardButton.WithCallbackData("Так", "problem_resolved") },
                        new[] { InlineKeyboardButton.WithCallbackData("Ні", "problem_not_resolved_1") },
                        new[] { InlineKeyboardButton.WithCallbackData("Повернутися в головне меню", "back_to_menu") }
                    }));
                    LogAction(chatId, "Перевірка швидкості (1-й етап)");
                    break;
                case "optimize_wifi":
                    UserData[chatId].AttemptedSolutions.Add("optimize_wifi");
                    await Bot.EditMessageTextAsync(chatId, UserData[chatId].MessageIdToEdit.Value, "Змініть канал Wi-Fi у налаштуваннях роутера.", replyMarkup: new InlineKeyboardMarkup(new[]
                    {
                        new[] { InlineKeyboardButton.WithCallbackData("Так", "problem_resolved") },
                        new[] { InlineKeyboardButton.WithCallbackData("Ні", "problem_not_resolved_1") },
                        new[] { InlineKeyboardButton.WithCallbackData("Повернутися в головне меню", "back_to_menu") }
                    }));
                    LogAction(chatId, "Оптимізація Wi-Fi (1-й етап)");
                    break;
                case "problem_resolved":
                    UserData[chatId].AttemptedSolutions.Clear();
                    if (UserData[chatId].MessageIdToEdit.HasValue)
                    {
                        await Bot.EditMessageTextAsync(chatId, UserData[chatId].MessageIdToEdit.Value, "Проблему вирішено!", replyMarkup: null);
                        UserData[chatId].MessageIdToEdit = null;
                    }
                    await Bot.SendTextMessageAsync(chatId, "Молодець! Проблему вирішено.");
                    UserStates[chatId] = UserState.MainMenu;
                    await ShowMainMenu(chatId);
                    LogAction(chatId, "Проблему вирішено");
                    break;
                case "problem_not_resolved_1":
                    if (UserData[chatId].MessageIdToEdit == null)
                    {
                        await Bot.SendTextMessageAsync(chatId, "Помилка: повідомлення для редагування не знайдено.");
                        LogAction(chatId, "Помилка: повідомлення для редагування не знайдено");
                        break;
                    }

                    InlineKeyboardMarkup solutionKeyboard = null;
                    string solutionMessage = "Спробуйте наступне:";

                    switch (UserData[chatId].ProblemType)
                    {
                        case "Проблема з оплатою":
                            if (!UserData[chatId].AttemptedSolutions.Contains("check_payment"))
                            {
                                UserData[chatId].AttemptedSolutions.Add("check_payment");
                                solutionKeyboard = new InlineKeyboardMarkup(new[]
                                {
                                    new[] { InlineKeyboardButton.WithCallbackData("Перевірити статус платежу", "check_payment") }
                                });
                                await Bot.EditMessageTextAsync(chatId, UserData[chatId].MessageIdToEdit.Value, solutionMessage, replyMarkup: solutionKeyboard);
                                LogAction(chatId, "Пропозиція першого рішення для оплати: check_payment");
                            }
                            else if (!UserData[chatId].AttemptedSolutions.Contains("pay_now"))
                            {
                                UserData[chatId].AttemptedSolutions.Add("pay_now");
                                solutionKeyboard = new InlineKeyboardMarkup(new[]
                                {
                                    new[] { InlineKeyboardButton.WithCallbackData("Оплатити зараз", "pay_now") }
                                });
                                await Bot.EditMessageTextAsync(chatId, UserData[chatId].MessageIdToEdit.Value, solutionMessage, replyMarkup: solutionKeyboard);
                                LogAction(chatId, "Пропозиція другого рішення для оплати: pay_now");
                            }
                            else
                            {
                                solutionKeyboard = new InlineKeyboardMarkup(new[]
                                {
                                    new[] { InlineKeyboardButton.WithCallbackData("Звернутися до оператора", "to_operator") },
                                    new[] { InlineKeyboardButton.WithCallbackData("Повернутися в головне меню", "back_to_menu") }
                                });
                                await Bot.EditMessageTextAsync(chatId, UserData[chatId].MessageIdToEdit.Value, "Ви вичерпали всі варіанти. Зверніться до оператора:", replyMarkup: solutionKeyboard);
                                LogAction(chatId, "Всі варіанти для оплати вичерпані");
                            }
                            break;
                        case "Проблема зі зв’язком":
                            if (!UserData[chatId].AttemptedSolutions.Contains("check_cable"))
                            {
                                UserData[chatId].AttemptedSolutions.Add("check_cable");
                                solutionKeyboard = new InlineKeyboardMarkup(new[]
                                {
                                    new[] { InlineKeyboardButton.WithCallbackData("Перевірити кабель", "check_cable") }
                                });
                                await Bot.EditMessageTextAsync(chatId, UserData[chatId].MessageIdToEdit.Value, solutionMessage, replyMarkup: solutionKeyboard);
                                LogAction(chatId, "Пропозиція першого рішення для зв’язку: check_cable");
                            }
                            else if (!UserData[chatId].AttemptedSolutions.Contains("restart_router"))
                            {
                                UserData[chatId].AttemptedSolutions.Add("restart_router");
                                solutionKeyboard = new InlineKeyboardMarkup(new[]
                                {
                                    new[] { InlineKeyboardButton.WithCallbackData("Перезапустити роутер", "restart_router") }
                                });
                                await Bot.EditMessageTextAsync(chatId, UserData[chatId].MessageIdToEdit.Value, solutionMessage, replyMarkup: solutionKeyboard);
                                LogAction(chatId, "Пропозиція другого рішення для зв’язку: restart_router");
                            }
                            else
                            {
                                solutionKeyboard = new InlineKeyboardMarkup(new[]
                                {
                                    new[] { InlineKeyboardButton.WithCallbackData("Звернутися до оператора", "to_operator") },
                                    new[] { InlineKeyboardButton.WithCallbackData("Повернутися в головне меню", "back_to_menu") }
                                });
                                await Bot.EditMessageTextAsync(chatId, UserData[chatId].MessageIdToEdit.Value, "Ви вичерпали всі варіанти. Зверніться до оператора:", replyMarkup: solutionKeyboard);
                                LogAction(chatId, "Всі варіанти для зв’язку вичерпані");
                            }
                            break;
                        case "Проблема з роутером":
                            if (!UserData[chatId].AttemptedSolutions.Contains("check_leds"))
                            {
                                UserData[chatId].AttemptedSolutions.Add("check_leds");
                                solutionKeyboard = new InlineKeyboardMarkup(new[]
                                {
                                    new[] { InlineKeyboardButton.WithCallbackData("Перевірити індикатори", "check_leds") }
                                });
                                await Bot.EditMessageTextAsync(chatId, UserData[chatId].MessageIdToEdit.Value, solutionMessage, replyMarkup: solutionKeyboard);
                                LogAction(chatId, "Пропозиція першого рішення для роутера: check_leds");
                            }
                            else if (!UserData[chatId].AttemptedSolutions.Contains("reset_router"))
                            {
                                UserData[chatId].AttemptedSolutions.Add("reset_router");
                                solutionKeyboard = new InlineKeyboardMarkup(new[]
                                {
                                    new[] { InlineKeyboardButton.WithCallbackData("Скинути налаштування", "reset_router") }
                                });
                                await Bot.EditMessageTextAsync(chatId, UserData[chatId].MessageIdToEdit.Value, solutionMessage, replyMarkup: solutionKeyboard);
                                LogAction(chatId, "Пропозиція другого рішення для роутера: reset_router");
                            }
                            else
                            {
                                solutionKeyboard = new InlineKeyboardMarkup(new[]
                                {
                                    new[] { InlineKeyboardButton.WithCallbackData("Звернутися до оператора", "to_operator") },
                                    new[] { InlineKeyboardButton.WithCallbackData("Повернутися в головне меню", "back_to_menu") }
                                });
                                await Bot.EditMessageTextAsync(chatId, UserData[chatId].MessageIdToEdit.Value, "Ви вичерпали всі варіанти. Зверніться до оператора:", replyMarkup: solutionKeyboard);
                                LogAction(chatId, "Всі варіанти для роутера вичерпані");
                            }
                            break;
                        case "Низька швидкість":
                            if (!UserData[chatId].AttemptedSolutions.Contains("check_speed"))
                            {
                                UserData[chatId].AttemptedSolutions.Add("check_speed");
                                solutionKeyboard = new InlineKeyboardMarkup(new[]
                                {
                                    new[] { InlineKeyboardButton.WithCallbackData("Перевірити швидкість", "check_speed") }
                                });
                                await Bot.EditMessageTextAsync(chatId, UserData[chatId].MessageIdToEdit.Value, solutionMessage, replyMarkup: solutionKeyboard);
                                LogAction(chatId, "Пропозиція першого рішення для швидкості: check_speed");
                            }
                            else if (!UserData[chatId].AttemptedSolutions.Contains("optimize_wifi"))
                            {
                                UserData[chatId].AttemptedSolutions.Add("optimize_wifi");
                                solutionKeyboard = new InlineKeyboardMarkup(new[]
                                {
                                    new[] { InlineKeyboardButton.WithCallbackData("Оптимізувати Wi-Fi", "optimize_wifi") }
                                });
                                await Bot.EditMessageTextAsync(chatId, UserData[chatId].MessageIdToEdit.Value, solutionMessage, replyMarkup: solutionKeyboard);
                                LogAction(chatId, "Пропозиція другого рішення для швидкості: optimize_wifi");
                            }
                            else
                            {
                                solutionKeyboard = new InlineKeyboardMarkup(new[]
                                {
                                    new[] { InlineKeyboardButton.WithCallbackData("Звернутися до оператора", "to_operator") },
                                    new[] { InlineKeyboardButton.WithCallbackData("Повернутися в головне меню", "back_to_menu") }
                                });
                                await Bot.EditMessageTextAsync(chatId, UserData[chatId].MessageIdToEdit.Value, "Ви вичерпали всі варіанти. Зверніться до оператора:", replyMarkup: solutionKeyboard);
                                LogAction(chatId, "Всі варіанти для швидкості вичерпані");
                            }
                            break;
                        default:
                            solutionKeyboard = new InlineKeyboardMarkup(new[]
                            {
                                new[] { InlineKeyboardButton.WithCallbackData("Звернутися до оператора", "to_operator") },
                                new[] { InlineKeyboardButton.WithCallbackData("Повернутися в головне меню", "back_to_menu") }
                            });
                            await Bot.EditMessageTextAsync(chatId, UserData[chatId].MessageIdToEdit.Value, "Ви вичерпали всі варіанти. Зверніться до оператора:", replyMarkup: solutionKeyboard);
                            LogAction(chatId, "Немає інших рішень для проблеми");
                            break;
                    }
                    break;
                case "to_operator":
                    if (UserData[chatId].MessageIdToEdit == null)
                    {
                        await Bot.SendTextMessageAsync(chatId, "Помилка: повідомлення для редагування не знайдено.");
                        LogAction(chatId, "Помилка: повідомлення для редагування не знайдено");
                        break;
                    }

                    if (!IsOperatorOnline)
                    {
                        var noOperatorKeyboard = new InlineKeyboardMarkup(new[]
                        {
                            new[] { InlineKeyboardButton.WithCallbackData("Повторити спробу", "retry_operator") },
                            new[] { InlineKeyboardButton.WithCallbackData("Повернутися в головне меню", "back_to_menu") }
                        });
                        await Bot.EditMessageTextAsync(chatId, UserData[chatId].MessageIdToEdit.Value, "Наразі оператор офлайн. Спробуйте пізніше або скористайтеся іншими опціями.", replyMarkup: noOperatorKeyboard);
                        LogAction(chatId, "Спроба звернення до оператора, але оператор офлайн");
                        break;
                    }
                    UserData[chatId].TicketId = GenerateTicketId();
                    SaveTicket(chatId, UserData[chatId].TicketId, UserData[chatId].ProblemType, UserData[chatId].ProblemDetails, "unanswered");
                    await NotifyAdmin(chatId, UserData[chatId]);
                    var operatorKeyboard = new InlineKeyboardMarkup(new[]
                    {
                        new[] { InlineKeyboardButton.WithCallbackData("Повернутися в головне меню", "back_to_menu") }
                    });
                    await Bot.EditMessageTextAsync(chatId, UserData[chatId].MessageIdToEdit.Value, "Ви в черзі на з’єднання з оператором. Очікуйте, будь ласка.", replyMarkup: operatorKeyboard);
                    LogAction(chatId, "Користувач у черзі на оператора");
                    await Task.Delay(5000);
                    UserStates[chatId] = UserState.OperatorConnected;
                    var connectedKeyboard = new InlineKeyboardMarkup(new[]
                    {
                        new[] { InlineKeyboardButton.WithCallbackData("Закрити заявку", "close_ticket") },
                        new[] { InlineKeyboardButton.WithCallbackData("Повернутися в головне меню", "back_to_menu") }
                    });
                    await Bot.SendTextMessageAsync(chatId, "Оператор підключений. Опишіть вашу проблему оператору:", replyMarkup: connectedKeyboard);
                    LogAction(chatId, $"Створено заявку: {UserData[chatId].TicketId}");
                    break;
                case "retry_operator":
                    if (UserData[chatId].MessageIdToEdit == null)
                    {
                        await Bot.SendTextMessageAsync(chatId, "Помилка: повідомлення для редагування не знайдено.");
                        LogAction(chatId, "Помилка: повідомлення для редагування не знайдено");
                        break;
                    }

                    if (!IsOperatorOnline)
                    {
                        var retryNoOperatorKeyboard = new InlineKeyboardMarkup(new[]
                        {
                            new[] { InlineKeyboardButton.WithCallbackData("Повторити спробу", "retry_operator") },
                            new[] { InlineKeyboardButton.WithCallbackData("Повернутися в головне меню", "back_to_menu") }
                        });
                        await Bot.EditMessageTextAsync(chatId, UserData[chatId].MessageIdToEdit.Value, "Оператор все ще офлайн. Спробувати ще раз?", replyMarkup: retryNoOperatorKeyboard);
                        LogAction(chatId, "Повторна спроба звернення до оператора, але оператор офлайн");
                        break;
                    }
                    UserData[chatId].TicketId = GenerateTicketId();
                    SaveTicket(chatId, UserData[chatId].TicketId, UserData[chatId].ProblemType, UserData[chatId].ProblemDetails, "unanswered");
                    await NotifyAdmin(chatId, UserData[chatId]);
                    var retryOperatorKeyboard = new InlineKeyboardMarkup(new[]
                    {
                        new[] { InlineKeyboardButton.WithCallbackData("Повернутися в головне меню", "back_to_menu") }
                    });
                    await Bot.EditMessageTextAsync(chatId, UserData[chatId].MessageIdToEdit.Value, "Ви в черзі на з’єднання з оператором. Очікуйте, будь ласка.", replyMarkup: retryOperatorKeyboard);
                    LogAction(chatId, "Користувач у черзі на оператора після повторної спроби");
                    await Task.Delay(5000);
                    UserStates[chatId] = UserState.OperatorConnected;
                    var retryConnectedKeyboard = new InlineKeyboardMarkup(new[]
                    {
                        new[] { InlineKeyboardButton.WithCallbackData("Закрити заявку", "close_ticket") },
                        new[] { InlineKeyboardButton.WithCallbackData("Повернутися в головне меню", "back_to_menu") }
                    });
                    await Bot.SendTextMessageAsync(chatId, "Оператор підключений. Опишіть вашу проблему оператору:", replyMarkup: retryConnectedKeyboard);
                    LogAction(chatId, $"Створено заявку після повторної спроби: {UserData[chatId].TicketId}");
                    break;
                case "confirm_issue":
                    if (UserData[chatId].MessageIdToEdit == null)
                    {
                        await Bot.SendTextMessageAsync(chatId, "Помилка: повідомлення для редагування не знайдено.");
                        LogAction(chatId, "Помилка: повідомлення для редагування не знайдено");
                        break;
                    }

                    if (!IsOperatorOnline)
                    {
                        var confirmNoOperatorKeyboard = new InlineKeyboardMarkup(new[]
                        {
                            new[] { InlineKeyboardButton.WithCallbackData("Повторити спробу", "retry_operator") },
                            new[] { InlineKeyboardButton.WithCallbackData("Повернутися в головне меню", "back_to_menu") }
                        });
                        await Bot.EditMessageTextAsync(chatId, UserData[chatId].MessageIdToEdit.Value, "Наразі оператор офлайн. Спробуйте пізніше або скористайтеся іншими опціями.", replyMarkup: confirmNoOperatorKeyboard);
                        LogAction(chatId, "Спроба підтвердження заявки, але оператор офлайн");
                        break;
                    }
                    UserData[chatId].TicketId = GenerateTicketId();
                    SaveTicket(chatId, UserData[chatId].TicketId, UserData[chatId].ProblemType, UserData[chatId].ProblemDetails, "unanswered");
                    await NotifyAdmin(chatId, UserData[chatId]);
                    var confirmOperatorKeyboard = new InlineKeyboardMarkup(new[]
                    {
                        new[] { InlineKeyboardButton.WithCallbackData("Повернутися в головне меню", "back_to_menu") }
                    });
                    await Bot.EditMessageTextAsync(chatId, UserData[chatId].MessageIdToEdit.Value, "Ви в черзі на з’єднання з оператором. Очікуйте, будь ласка.", replyMarkup: confirmOperatorKeyboard);
                    LogAction(chatId, "Користувач у черзі на оператора");
                    await Task.Delay(5000);
                    UserStates[chatId] = UserState.OperatorConnected;
                    var confirmConnectedKeyboard = new InlineKeyboardMarkup(new[]
                    {
                        new[] { InlineKeyboardButton.WithCallbackData("Закрити заявку", "close_ticket") },
                        new[] { InlineKeyboardButton.WithCallbackData("Повернутися в головне меню", "back_to_menu") }
                    });
                    await Bot.SendTextMessageAsync(chatId, "Оператор підключений. Опишіть вашу проблему оператору:", replyMarkup: confirmConnectedKeyboard);
                    LogAction(chatId, $"Підтверджено заявку: {UserData[chatId].TicketId}");
                    break;
                case "cancel_issue":
                    UserData[chatId].ProblemDetails = null;
                    UserStates[chatId] = UserState.MainMenu;
                    await ShowMainMenu(chatId);
                    LogAction(chatId, "Скасовано опис проблеми");
                    break;
                case "close_ticket":
                    if (UserData[chatId].TicketId != null)
                    {
                        UpdateTicketStatus(UserData[chatId].TicketId, "closed");
                        TicketMessageIds.Remove(UserData[chatId].TicketId);
                        TicketChatIds.Remove(UserData[chatId].TicketId);
                        await Bot.SendTextMessageAsync(AdminChatId, $"Заявка {UserData[chatId].TicketId} закрита користувачем.");
                        LogAction(chatId, $"Заявка {UserData[chatId].TicketId} закрита");
                        UserData[chatId].TicketId = null;
                    }
                    await Bot.SendTextMessageAsync(chatId, "Заявку закрито.");
                    UserStates[chatId] = UserState.MainMenu;
                    await ShowMainMenu(chatId);
                    break;
                case "operator_response_yes":
                    var yesKeyboard = new InlineKeyboardMarkup(new[]
                    {
                        new[] { InlineKeyboardButton.WithCallbackData("Закрити заявку", "close_ticket") },
                        new[] { InlineKeyboardButton.WithCallbackData("Повернутися в головне меню", "back_to_menu") }
                    });
                    await Bot.SendTextMessageAsync(chatId, "Чудово! Чи є ще питання?", replyMarkup: yesKeyboard);
                    LogAction(chatId, "Відповідь оператора допомогла");
                    break;
                case "operator_response_no":
                    await Bot.SendTextMessageAsync(chatId, "Вибачте, що не допомогло. Спробуйте описати проблему детальніше оператору.");
                    UserStates[chatId] = UserState.OperatorConnected;
                    LogAction(chatId, "Відповідь оператора не допомогла");
                    break;
            }

            await Bot.AnswerCallbackQueryAsync(callback.Id);
        }

        static Task HandleErrorAsync(ITelegramBotClient botClient, Exception exception, CancellationToken cancellationToken)
        {
            Log.Error(exception, "Помилка в боті");
            return Task.CompletedTask;
        }

        public static async Task Main()
        {
            SetupLogging();
            SetupDatabase();
            Bot = new TelegramBotClient(BotToken);
            Bot.StartReceiving(HandleUpdateAsync, HandleErrorAsync);
            Console.WriteLine("Бот запущено...");
            await Task.Delay(-1);
        }
    }

    public class UserData
    {
        public string? ContractNumber { get; set; }
        public string? Address { get; set; }
        public bool IsIdentified { get; set; }
        public string? TicketId { get; set; }
        public string? ProblemType { get; set; }
        public string? ProblemDetails { get; set; }
        public HashSet<string> AttemptedSolutions { get; set; } = new HashSet<string>();
        public int? MessageIdToEdit { get; set; }
    }
}