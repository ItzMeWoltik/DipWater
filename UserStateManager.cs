using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace InternetSupportBot
{
    public class UserStateManager
    {
        private readonly Dictionary<long, UserState> _userStates = new();
        private readonly Dictionary<long, UserData> _userData = new();
        private readonly DatabaseService _databaseService;

        public UserStateManager(DatabaseService databaseService)
        {
            _databaseService = databaseService;
        }

        public async Task HandleUserMessageAsync(ITelegramBotClient bot, Message message)
        {
            var chatId = message.Chat.Id;

            if (!_userStates.ContainsKey(chatId))
                _userStates[chatId] = UserState.Start;
            if (!_userData.ContainsKey(chatId))
                _userData[chatId] = new UserData();

            var state = _userStates[chatId];
            var data = _userData[chatId];

            switch (state)
            {
                case UserState.Start:
                    await HandleStartAsync(bot, message);
                    break;
                case UserState.IdentificationMethod:
                    await HandleIdentificationMethodAsync(bot, message);
                    break;
                case UserState.EnterContractNumber:
                    await HandleContractNumberAsync(bot, message);
                    break;
                case UserState.EnterAddress:
                    await HandleAddressAsync(bot, message);
                    break;
                case UserState.MainMenu:
                    await HandleMainMenuAsync(bot, message);
                    break;
                case UserState.Help:
                    await HandleHelpAsync(bot, message);
                    break;
                case UserState.CheckBalance:
                    await HandleCheckBalanceAsync(bot, message);
                    break;
                case UserState.TechnicalSupport:
                    await HandleTechnicalSupportAsync(bot, message);
                    break;
                case UserState.ProblemType:
                    await HandleProblemTypeAsync(bot, message);
                    break;
                case UserState.PaymentIssue:
                    await HandlePaymentIssueAsync(bot, message);
                    break;
                case UserState.ConnectionIssue:
                    await HandleConnectionIssueAsync(bot, message);
                    break;
                case UserState.RouterIssue:
                    await HandleRouterIssueAsync(bot, message);
                    break;
                case UserState.SpeedIssue:
                    await HandleSpeedIssueAsync(bot, message);
                    break;
                case UserState.OtherIssue:
                    await HandleOtherIssueAsync(bot, message);
                    break;
                case UserState.ConfirmIssue:
                    await HandleConfirmIssueAsync(bot, message);
                    break;
                case UserState.WaitingOperator:
                    await HandleWaitingOperatorAsync(bot, message);
                    break;
                case UserState.OperatorConnected:
                    await HandleOperatorConnectedAsync(bot, message, null);
                    break;
                case UserState.ProblemNotResolved1:
                    await HandleProblemNotResolved1Async(bot, message);
                    break;
                case UserState.ProblemNotResolved2:
                    await HandleProblemNotResolved2Async(bot, message);
                    break;
                default:
                    var keyboard = new InlineKeyboardMarkup(new[]
                    {
                        new[] { InlineKeyboardButton.WithCallbackData("Повернутися в головне меню", "back_to_menu") }
                    });
                    await bot.SendTextMessageAsync(chatId, "Невідомий запит. Оберіть дію нижче:", replyMarkup: keyboard);
                    _databaseService.LogAction(chatId, "Невідомий запит");
                    break;
            }
        }

        public async Task HandleCallbackQueryAsync(ITelegramBotClient bot, CallbackQuery callback, TicketManager ticketManager)
        {
            if (callback.Message == null || callback.Data == null) return;

            var chatId = callback.Message.Chat.Id;
            var data = callback.Data;
            _databaseService.LogAction(chatId, $"Отримано callback: {data}");

            switch (data)
            {
                case "back_to_menu":
                    _userStates[chatId] = UserState.MainMenu;
                    _userData[chatId].AttemptedSolutions = new HashSet<string>();
                    _userData[chatId].MessageIdToEdit = null;
                    await ShowMainMenuAsync(bot, chatId);
                    _databaseService.LogAction(chatId, "Повернення в головне меню");
                    break;
                case "by_contract":
                    _userStates[chatId] = UserState.EnterContractNumber;
                    var contractKeyboard = new InlineKeyboardMarkup(new[]
                    {
                        new[] { InlineKeyboardButton.WithCallbackData("Повернутися в головне меню", "back_to_menu") }
                    });
                    await bot.SendTextMessageAsync(chatId, "Введіть номер угоди:", replyMarkup: contractKeyboard);
                    _databaseService.LogAction(chatId, "Обрано ідентифікацію за номером угоди");
                    break;
                case "by_address":
                    _userStates[chatId] = UserState.EnterAddress;
                    var addressKeyboard = new InlineKeyboardMarkup(new[]
                    {
                        new[] { InlineKeyboardButton.WithCallbackData("Повернутися в головне меню", "back_to_menu") }
                    });
                    await bot.SendTextMessageAsync(chatId, "Введіть адресу (наприклад, вул. Центральна, 10, кв. 5):", replyMarkup: addressKeyboard);
                    _databaseService.LogAction(chatId, "Обрано ідентифікацію за адресою");
                    break;
                case "help":
                    await HandleHelpAsync(bot, callback.Message);
                    break;
                case "check_balance":
                    await HandleCheckBalanceAsync(bot, callback.Message);
                    break;
                case "technical_support":
                    await HandleTechnicalSupportAsync(bot, callback.Message);
                    break;
                case "payment_issue":
                    _userData[chatId].ProblemType = "Проблема з оплатою";
                    _userData[chatId].AttemptedSolutions = new HashSet<string>();
                    var paymentKeyboard = new InlineKeyboardMarkup(new[]
                    {
                        new[] { InlineKeyboardButton.WithCallbackData("Перевірити статус платежу", "check_payment") }
                    });
                    var paymentMessage = await bot.SendTextMessageAsync(chatId, "Проблема з оплатою. Спробуйте наступне:", replyMarkup: paymentKeyboard);
                    _userData[chatId].MessageIdToEdit = paymentMessage.MessageId;
                    _userStates[chatId] = UserState.ProblemNotResolved1;
                    _databaseService.LogAction(chatId, "Запит на вирішення проблеми з оплатою");
                    break;
                case "connection_issue":
                    _userData[chatId].ProblemType = "Проблема зі зв’язком";
                    _userData[chatId].AttemptedSolutions = new HashSet<string>();
                    var connectionKeyboard = new InlineKeyboardMarkup(new[]
                    {
                        new[] { InlineKeyboardButton.WithCallbackData("Перевірити кабель", "check_cable") }
                    });
                    var connectionMessage = await bot.SendTextMessageAsync(chatId, "Проблема зі зв’язком. Спробуйте наступне:", replyMarkup: connectionKeyboard);
                    _userData[chatId].MessageIdToEdit = connectionMessage.MessageId;
                    _userStates[chatId] = UserState.ProblemNotResolved1;
                    _databaseService.LogAction(chatId, "Запит на вирішення проблеми зі зв’язком");
                    break;
                case "router_issue":
                    _userData[chatId].ProblemType = "Проблема з роутером";
                    _userData[chatId].AttemptedSolutions = new HashSet<string>();
                    var routerKeyboard = new InlineKeyboardMarkup(new[]
                    {
                        new[] { InlineKeyboardButton.WithCallbackData("Перевірити індикатори", "check_leds") }
                    });
                    var routerMessage = await bot.SendTextMessageAsync(chatId, "Проблема з роутером. Спробуйте наступне:", replyMarkup: routerKeyboard);
                    _userData[chatId].MessageIdToEdit = routerMessage.MessageId;
                    _userStates[chatId] = UserState.ProblemNotResolved1;
                    _databaseService.LogAction(chatId, "Запит на вирішення проблеми з роутером");
                    break;
                case "speed_issue":
                    _userData[chatId].ProblemType = "Низька швидкість";
                    _userData[chatId].AttemptedSolutions = new HashSet<string>();
                    var speedKeyboard = new InlineKeyboardMarkup(new[]
                    {
                        new[] { InlineKeyboardButton.WithCallbackData("Перевірити швидкість", "check_speed") }
                    });
                    var speedMessage = await bot.SendTextMessageAsync(chatId, "Низька швидкість інтернету. Спробуйте наступне:", replyMarkup: speedKeyboard);
                    _userData[chatId].MessageIdToEdit = speedMessage.MessageId;
                    _userStates[chatId] = UserState.ProblemNotResolved1;
                    _databaseService.LogAction(chatId, "Запит на вирішення проблеми з низькою швидкістю");
                    break;
                case "other_issue":
                    _userData[chatId].ProblemType = "Інше";
                    _userData[chatId].AttemptedSolutions = new HashSet<string>();
                    var otherKeyboard = new InlineKeyboardMarkup(new[]
                    {
                        new[] { InlineKeyboardButton.WithCallbackData("Повернутися в головне меню", "back_to_menu") }
                    });
                    await bot.SendTextMessageAsync(chatId, "Опишіть вашу проблему:", replyMarkup: otherKeyboard);
                    _userStates[chatId] = UserState.OtherIssue;
                    _databaseService.LogAction(chatId, "Запит на опис іншої проблеми");
                    break;
                case "check_payment":
                    _userData[chatId].AttemptedSolutions.Add("check_payment");
                    await bot.EditMessageTextAsync(chatId, _userData[chatId].MessageIdToEdit.Value, "Перевірте статус платежу в особистому кабінеті або повторіть оплату.", replyMarkup: new InlineKeyboardMarkup(new[]
                    {
                        new[] { InlineKeyboardButton.WithCallbackData("Так", "problem_resolved") },
                        new[] { InlineKeyboardButton.WithCallbackData("Ні", "problem_not_resolved_1") },
                        new[] { InlineKeyboardButton.WithCallbackData("Повернутися в головне меню", "back_to_menu") }
                    }));
                    _databaseService.LogAction(chatId, "Перевірка статусу платежу (1-й етап)");
                    break;
                case "pay_now":
                    _userData[chatId].AttemptedSolutions.Add("pay_now");
                    await bot.EditMessageTextAsync(chatId, _userData[chatId].MessageIdToEdit.Value, "Перейдіть до оплати за посиланням: [Оплатити](https://example.com/pay).", replyMarkup: new InlineKeyboardMarkup(new[]
                    {
                        new[] { InlineKeyboardButton.WithCallbackData("Так", "problem_resolved") },
                        new[] { InlineKeyboardButton.WithCallbackData("Ні", "problem_not_resolved_1") },
                        new[] { InlineKeyboardButton.WithCallbackData("Повернутися в головне меню", "back_to_menu") }
                    }));
                    _databaseService.LogAction(chatId, "Запит на оплату (1-й етап)");
                    break;
                case "check_cable":
                    _userData[chatId].AttemptedSolutions.Add("check_cable");
                    await bot.EditMessageTextAsync(chatId, _userData[chatId].MessageIdToEdit.Value, "Перевірте, чи кабель підключений до роутера та комп’ютера.", replyMarkup: new InlineKeyboardMarkup(new[]
                    {
                        new[] { InlineKeyboardButton.WithCallbackData("Так", "problem_resolved") },
                        new[] { InlineKeyboardButton.WithCallbackData("Ні", "problem_not_resolved_1") },
                        new[] { InlineKeyboardButton.WithCallbackData("Повернутися в головне меню", "back_to_menu") }
                    }));
                    _databaseService.LogAction(chatId, "Перевірка кабелю (1-й етап)");
                    break;
                case "restart_router":
                    _userData[chatId].AttemptedSolutions.Add("restart_router");
                    await bot.EditMessageTextAsync(chatId, _userData[chatId].MessageIdToEdit.Value, "Вимкніть роутер на 10 секунд і увімкніть знову.", replyMarkup: new InlineKeyboardMarkup(new[]
                    {
                        new[] { InlineKeyboardButton.WithCallbackData("Так", "problem_resolved") },
                        new[] { InlineKeyboardButton.WithCallbackData("Ні", "problem_not_resolved_1") },
                        new[] { InlineKeyboardButton.WithCallbackData("Повернутися в головне меню", "back_to_menu") }
                    }));
                    _databaseService.LogAction(chatId, "Перезапуск роутера (1-й етап)");
                    break;
                case "check_leds":
                    _userData[chatId].AttemptedSolutions.Add("check_leds");
                    await bot.EditMessageTextAsync(chatId, _userData[chatId].MessageIdToEdit.Value, "Перевірте, чи горять зелені індикатори на роутері.", replyMarkup: new InlineKeyboardMarkup(new[]
                    {
                        new[] { InlineKeyboardButton.WithCallbackData("Так", "problem_resolved") },
                        new[] { InlineKeyboardButton.WithCallbackData("Ні", "problem_not_resolved_1") },
                        new[] { InlineKeyboardButton.WithCallbackData("Повернутися в головне меню", "back_to_menu") }
                    }));
                    _databaseService.LogAction(chatId, "Перевірка індикаторів роутера (1-й етап)");
                    break;
                case "reset_router":
                    _userData[chatId].AttemptedSolutions.Add("reset_router");
                    await bot.EditMessageTextAsync(chatId, _userData[chatId].MessageIdToEdit.Value, "Натисніть кнопку Reset на роутері на 5 секунд.", replyMarkup: new InlineKeyboardMarkup(new[]
                    {
                        new[] { InlineKeyboardButton.WithCallbackData("Так", "problem_resolved") },
                        new[] { InlineKeyboardButton.WithCallbackData("Ні", "problem_not_resolved_1") },
                        new[] { InlineKeyboardButton.WithCallbackData("Повернутися в головне меню", "back_to_menu") }
                    }));
                    _databaseService.LogAction(chatId, "Скидання налаштувань роутера (1-й етап)");
                    break;
                case "check_speed":
                    _userData[chatId].AttemptedSolutions.Add("check_speed");
                    await bot.EditMessageTextAsync(chatId, _userData[chatId].MessageIdToEdit.Value, "Перевірте швидкість на сайті speedtest.net.", replyMarkup: new InlineKeyboardMarkup(new[]
                    {
                        new[] { InlineKeyboardButton.WithCallbackData("Так", "problem_resolved") },
                        new[] { InlineKeyboardButton.WithCallbackData("Ні", "problem_not_resolved_1") },
                        new[] { InlineKeyboardButton.WithCallbackData("Повернутися в головне меню", "back_to_menu") }
                    }));
                    _databaseService.LogAction(chatId, "Перевірка швидкості (1-й етап)");
                    break;
                case "optimize_wifi":
                    _userData[chatId].AttemptedSolutions.Add("optimize_wifi");
                    await bot.EditMessageTextAsync(chatId, _userData[chatId].MessageIdToEdit.Value, "Змініть канал Wi-Fi у налаштуваннях роутера.", replyMarkup: new InlineKeyboardMarkup(new[]
                    {
                        new[] { InlineKeyboardButton.WithCallbackData("Так", "problem_resolved") },
                        new[] { InlineKeyboardButton.WithCallbackData("Ні", "problem_not_resolved_1") },
                        new[] { InlineKeyboardButton.WithCallbackData("Повернутися в головне меню", "back_to_menu") }
                    }));
                    _databaseService.LogAction(chatId, "Оптимізація Wi-Fi (1-й етап)");
                    break;
                case "problem_resolved":
                    _userData[chatId].AttemptedSolutions.Clear();
                    if (_userData[chatId].MessageIdToEdit.HasValue)
                    {
                        await bot.EditMessageTextAsync(chatId, _userData[chatId].MessageIdToEdit.Value, "Проблему вирішено!", replyMarkup: null);
                        _userData[chatId].MessageIdToEdit = null;
                    }
                    await bot.SendTextMessageAsync(chatId, "Молодець! Проблему вирішено.");
                    _userStates[chatId] = UserState.MainMenu;
                    await ShowMainMenuAsync(bot, chatId);
                    _databaseService.LogAction(chatId, "Проблему вирішено");
                    break;
                case "problem_not_resolved_1":
                    await HandleProblemNotResolved1Async(bot, callback.Message);
                    break;
                case "to_operator":
                    await HandleToOperatorAsync(bot, chatId, ticketManager);
                    break;
                case "retry_operator":
                    await HandleRetryOperatorAsync(bot, chatId, ticketManager);
                    break;
                case "confirm_issue":
                    await HandleConfirmIssueAsync(bot, callback.Message, ticketManager);
                    break;
                case "cancel_issue":
                    _userData[chatId].ProblemDetails = null;
                    _userStates[chatId] = UserState.MainMenu;
                    await ShowMainMenuAsync(bot, chatId);
                    _databaseService.LogAction(chatId, "Скасовано опис проблеми");
                    break;
                case "close_ticket":
                    await ticketManager.CloseTicketAsync(bot, chatId, _userData[chatId].TicketId);
                    _userData[chatId].TicketId = null;
                    await bot.SendTextMessageAsync(chatId, "Заявку закрито.");
                    _userStates[chatId] = UserState.MainMenu;
                    await ShowMainMenuAsync(bot, chatId);
                    break;
                case "operator_response_yes":
                    var yesKeyboard = new InlineKeyboardMarkup(new[]
                    {
                        new[] { InlineKeyboardButton.WithCallbackData("Закрити заявку", "close_ticket") },
                        new[] { InlineKeyboardButton.WithCallbackData("Повернутися в головне меню", "back_to_menu") }
                    });
                    await bot.SendTextMessageAsync(chatId, "Чудово! Чи є ще питання?", replyMarkup: yesKeyboard);
                    _databaseService.LogAction(chatId, "Відповідь оператора допомогла");
                    break;
                case "operator_response_no":
                    await bot.SendTextMessageAsync(chatId, "Вибачте, що не допомогло. Спробуйте описати проблему детальніше оператору.");
                    _userStates[chatId] = UserState.OperatorConnected;
                    _databaseService.LogAction(chatId, "Відповідь оператора не допомогла");
                    break;
            }

            await bot.AnswerCallbackQueryAsync(callback.Id);
        }

        private async Task HandleStartAsync(ITelegramBotClient bot, Message message)
        {
            var chatId = message.Chat.Id;
            _userStates[chatId] = UserState.IdentificationMethod;
            var keyboard = new InlineKeyboardMarkup(new[]
            {
                new[] { InlineKeyboardButton.WithCallbackData("За номером угоди", "by_contract") },
                new[] { InlineKeyboardButton.WithCallbackData("За адресою", "by_address") }
            });
            await bot.SendTextMessageAsync(chatId, "Вітаємо! Будь ласка, оберіть спосіб ідентифікації:", replyMarkup: keyboard);
            _databaseService.LogAction(chatId, "Запит на ідентифікацію");
        }

        private async Task HandleIdentificationMethodAsync(ITelegramBotClient bot, Message message)
        {
            var chatId = message.Chat.Id;
            var keyboard = new InlineKeyboardMarkup(new[]
            {
                new[] { InlineKeyboardButton.WithCallbackData("Повернутися в головне меню", "back_to_menu") }
            });
            await bot.SendTextMessageAsync(chatId, "Оберіть спосіб ідентифікації за допомогою кнопок вище.", replyMarkup: keyboard);
            _databaseService.LogAction(chatId, "Невірний вибір методу ідентифікації");
        }

        private async Task HandleContractNumberAsync(ITelegramBotClient bot, Message message)
        {
            var chatId = message.Chat.Id;
            var contractNumber = message.Text;
            if (_databaseService.CheckUserExists(chatId, contractNumber: contractNumber))
            {
                _userData[chatId].ContractNumber = contractNumber;
                _userData[chatId].IsIdentified = true;
                _userStates[chatId] = UserState.MainMenu;
                await ShowMainMenuAsync(bot, chatId);
                _databaseService.LogAction(chatId, $"Ідентифікація успішна за номером угоди: {contractNumber}");
            }
            else
            {
                _databaseService.SaveUser(chatId, contractNumber, "");
                _userData[chatId].ContractNumber = contractNumber;
                _userData[chatId].IsIdentified = true;
                _userStates[chatId] = UserState.MainMenu;
                await ShowMainMenuAsync(bot, chatId);
                _databaseService.LogAction(chatId, $"Новий користувач зареєстрований за номером угоди: {contractNumber}");
            }
        }

        private async Task HandleAddressAsync(ITelegramBotClient bot, Message message)
        {
            var chatId = message.Chat.Id;
            var address = message.Text;
            if (_databaseService.CheckUserExists(chatId, address: address))
            {
                _userData[chatId].Address = address;
                _userData[chatId].IsIdentified = true;
                _userStates[chatId] = UserState.MainMenu;
                await ShowMainMenuAsync(bot, chatId);
                _databaseService.LogAction(chatId, $"Ідентифікація успішна за адресою: {address}");
            }
            else
            {
                _databaseService.SaveUser(chatId, "", address);
                _userData[chatId].Address = address;
                _userData[chatId].IsIdentified = true;
                _userStates[chatId] = UserState.MainMenu;
                await ShowMainMenuAsync(bot, chatId);
                _databaseService.LogAction(chatId, $"Новий користувач зареєстрований за адресою: {address}");
            }
        }

        private async Task ShowMainMenuAsync(ITelegramBotClient bot, long chatId)
        {
            var keyboard = new InlineKeyboardMarkup(new[]
            {
                new[] { InlineKeyboardButton.WithCallbackData("Довідка", "help") },
                new[] { InlineKeyboardButton.WithCallbackData("Перевірити баланс", "check_balance") },
                new[] { InlineKeyboardButton.WithCallbackData("Технічна підтримка", "technical_support") }
            });
            await bot.SendTextMessageAsync(chatId, "Оберіть потрібну дію:", replyMarkup: keyboard);
            _databaseService.LogAction(chatId, "Показане головне меню");
        }

        private async Task HandleMainMenuAsync(ITelegramBotClient bot, Message message)
        {
            var chatId = message.Chat.Id;
            var keyboard = new InlineKeyboardMarkup(new[]
            {
                new[] { InlineKeyboardButton.WithCallbackData("Повернутися в головне меню", "back_to_menu") }
            });
            await bot.SendTextMessageAsync(chatId, "Оберіть дію за допомогою кнопок вище.", replyMarkup: keyboard);
            _databaseService.LogAction(chatId, "Невірний вибір у головному меню");
        }

        private async Task HandleHelpAsync(ITelegramBotClient bot, Message message)
        {
            var chatId = message.Chat.Id;
            var helpText = "📚 **Довідка**\n\n" +
                           "- **Перевірити баланс**: Дізнайтесь стан вашого рахунку.\n" +
                           "- **Технічна підтримка**: Отримайте допомогу з проблемами інтернету чи роутера.\n" +
                           "Оберіть дію у головному меню.";
            var keyboard = new InlineKeyboardMarkup(new[]
            {
                new[] { InlineKeyboardButton.WithCallbackData("Повернутися в головне меню", "back_to_menu") }
            });
            await bot.SendTextMessageAsync(chatId, helpText, parseMode: ParseMode.Markdown, replyMarkup: keyboard);
            _userStates[chatId] = UserState.MainMenu;
            _databaseService.LogAction(chatId, "Показана довідка");
        }

        private async Task HandleCheckBalanceAsync(ITelegramBotClient bot, Message message)
        {
            var chatId = message.Chat.Id;
            var balance = new Random().Next(0, 1000);
            var keyboard = new InlineKeyboardMarkup(new[]
            {
                new[] { InlineKeyboardButton.WithCallbackData("Повернутися в головне меню", "back_to_menu") }
            });
            await bot.SendTextMessageAsync(chatId, $"Ваш баланс: {balance} грн.", replyMarkup: keyboard);
            _userStates[chatId] = UserState.MainMenu;
            _databaseService.LogAction(chatId, $"Перевірка балансу: {balance} грн");
        }

        private async Task HandleTechnicalSupportAsync(ITelegramBotClient bot, Message message)
        {
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
            await bot.SendTextMessageAsync(chatId, "Оберіть тип проблеми:", replyMarkup: keyboard);
            _userStates[chatId] = UserState.ProblemType;
            _databaseService.LogAction(chatId, "Запит на вибір типу проблеми");
        }

        private async Task HandleProblemTypeAsync(ITelegramBotClient bot, Message message)
        {
            var chatId = message.Chat.Id;
            var keyboard = new InlineKeyboardMarkup(new[]
            {
                new[] { InlineKeyboardButton.WithCallbackData("Повернутися в головне меню", "back_to_menu") }
            });
            await bot.SendTextMessageAsync(chatId, "Оберіть тип проблеми за допомогою кнопок вище.", replyMarkup: keyboard);
            _databaseService.LogAction(chatId, "Невірний вибір типу проблеми");
        }

        private async Task HandlePaymentIssueAsync(ITelegramBotClient bot, Message message)
        {
            var chatId = message.Chat.Id;
            var keyboard = new InlineKeyboardMarkup(new[]
            {
                new[] { InlineKeyboardButton.WithCallbackData("Перевірити статус платежу", "check_payment") }
            });
            var paymentMessage = await bot.SendTextMessageAsync(chatId, "Проблема з оплатою. Спробуйте наступне:", replyMarkup: keyboard);
            _userData[chatId].MessageIdToEdit = paymentMessage.MessageId;
            _userStates[chatId] = UserState.ProblemNotResolved1;
            _databaseService.LogAction(chatId, "Запит на вирішення проблеми з оплатою");
        }

        private async Task HandleConnectionIssueAsync(ITelegramBotClient bot, Message message)
        {
            var chatId = message.Chat.Id;
            var keyboard = new InlineKeyboardMarkup(new[]
            {
                new[] { InlineKeyboardButton.WithCallbackData("Перевірити кабель", "check_cable") }
            });
            var connectionMessage = await bot.SendTextMessageAsync(chatId, "Проблема зі зв’язком. Спробуйте наступне:", replyMarkup: keyboard);
            _userData[chatId].MessageIdToEdit = connectionMessage.MessageId;
            _userStates[chatId] = UserState.ProblemNotResolved1;
            _databaseService.LogAction(chatId, "Запит на вирішення проблеми зі зв’язком");
        }

        private async Task HandleRouterIssueAsync(ITelegramBotClient bot, Message message)
        {
            var chatId = message.Chat.Id;
            var keyboard = new InlineKeyboardMarkup(new[]
            {
                new[] { InlineKeyboardButton.WithCallbackData("Перевірити індикатори", "check_leds") }
            });
            var routerMessage = await bot.SendTextMessageAsync(chatId, "Проблема з роутером. Спробуйте наступне:", replyMarkup: keyboard);
            _userData[chatId].MessageIdToEdit = routerMessage.MessageId;
            _userStates[chatId] = UserState.ProblemNotResolved1;
            _databaseService.LogAction(chatId, "Запит на вирішення проблеми з роутером");
        }

        private async Task HandleSpeedIssueAsync(ITelegramBotClient bot, Message message)
        {
            var chatId = message.Chat.Id;
            var keyboard = new InlineKeyboardMarkup(new[]
            {
                new[] { InlineKeyboardButton.WithCallbackData("Перевірити швидкість", "check_speed") }
            });
            var speedMessage = await bot.SendTextMessageAsync(chatId, "Низька швидкість інтернету. Спробуйте наступне:", replyMarkup: keyboard);
            _userData[chatId].MessageIdToEdit = speedMessage.MessageId;
            _userStates[chatId] = UserState.ProblemNotResolved1;
            _databaseService.LogAction(chatId, "Запит на вирішення проблеми з низькою швидкістю");
        }

        private async Task HandleOtherIssueAsync(ITelegramBotClient bot, Message message)
        {
            var chatId = message.Chat.Id;
            _userData[chatId].ProblemDetails = message.Text;
            var keyboard = new InlineKeyboardMarkup(new[]
            {
                new[] { InlineKeyboardButton.WithCallbackData("Підтвердити", "confirm_issue") },
                new[] { InlineKeyboardButton.WithCallbackData("Скасувати", "cancel_issue") },
                new[] { InlineKeyboardButton.WithCallbackData("Повернутися в головне меню", "back_to_menu") }
            });
            await bot.SendTextMessageAsync(chatId, $"Ви описали проблему: {_userData[chatId].ProblemDetails}. Підтвердити?", replyMarkup: keyboard);
            _userStates[chatId] = UserState.ConfirmIssue;
            _databaseService.LogAction(chatId, $"Опис проблеми: {_userData[chatId].ProblemDetails}");
        }

        private async Task HandleConfirmIssueAsync(ITelegramBotClient bot, Message message, TicketManager ticketManager = null)
        {
            var chatId = message.Chat.Id;
            _userData[chatId].ProblemDetails = message.Text;
            var keyboard = new InlineKeyboardMarkup(new[]
            {
                new[] { InlineKeyboardButton.WithCallbackData("Підтвердити", "confirm_issue") },
                new[] { InlineKeyboardButton.WithCallbackData("Скасувати", "cancel_issue") },
                new[] { InlineKeyboardButton.WithCallbackData("Повернутися в головне меню", "back_to_menu") }
            });
            await bot.SendTextMessageAsync(chatId, $"Ви описали проблему: {_userData[chatId].ProblemDetails}. Підтвердити?", replyMarkup: keyboard);
            _databaseService.LogAction(chatId, $"Опис проблеми: {_userData[chatId].ProblemDetails}");
        }

        private async Task HandleWaitingOperatorAsync(ITelegramBotClient bot, Message message)
        {
            var chatId = message.Chat.Id;
            var keyboard = new InlineKeyboardMarkup(new[]
            {
                new[] { InlineKeyboardButton.WithCallbackData("Повернутися в головне меню", "back_to_menu") }
            });
            await bot.SendTextMessageAsync(chatId, "Ви в черзі на з’єднання з оператором. Очікуйте, будь ласка.", replyMarkup: keyboard);
            _databaseService.LogAction(chatId, "Користувач у черзі на оператора");
            await Task.Delay(5000);
            _userStates[chatId] = UserState.OperatorConnected;
            var operatorKeyboard = new InlineKeyboardMarkup(new[]
            {
                new[] { InlineKeyboardButton.WithCallbackData("Закрити заявку", "close_ticket") },
                new[] { InlineKeyboardButton.WithCallbackData("Повернутися в головне меню", "back_to_menu") }
            });
            await bot.SendTextMessageAsync(chatId, "Оператор підключений. Опишіть вашу проблему оператору:", replyMarkup: operatorKeyboard);
            _databaseService.LogAction(chatId, "Оператор підключений");
        }

        private async Task HandleOperatorConnectedAsync(ITelegramBotClient bot, Message message, TicketManager ticketManager)
        {
            var chatId = message.Chat.Id;
            if (_userData[chatId].TicketId == null) return;

            var ticketId = _userData[chatId].TicketId;
            var userInfo = _userData[chatId];
            var identification = !string.IsNullOrEmpty(userInfo.ContractNumber)
                ? $"Номер угоди: {userInfo.ContractNumber}"
                : $"Адреса: {userInfo.Address}";
            
            var updatedMessage = $"{ticketManager.GetLastMessage(ticketId)}\nДодаткове повідомлення: {message.Text}";
            await ticketManager.UpdateAdminMessageAsync(bot, ticketId, updatedMessage);
            
            var keyboard = new InlineKeyboardMarkup(new[]
            {
                new[] { InlineKeyboardButton.WithCallbackData("Закрити заявку", "close_ticket") },
                new[] { InlineKeyboardButton.WithCallbackData("Повернутися в головне меню", "back_to_menu") }
            });
            await bot.SendTextMessageAsync(chatId, "Ваше повідомлення передано оператору. Очікуйте відповіді.", replyMarkup: keyboard);
            _databaseService.LogAction(chatId, $"Повідомлення оператору: {message.Text}");
        }

        private async Task HandleProblemNotResolved1Async(ITelegramBotClient bot, Message message)
        {
            var chatId = message.Chat.Id;
            var keyboard = new InlineKeyboardMarkup(new[]
            {
                new[] { InlineKeyboardButton.WithCallbackData("Так", "problem_resolved") },
                new[] { InlineKeyboardButton.WithCallbackData("Ні", "problem_not_resolved_1") },
                new[] { InlineKeyboardButton.WithCallbackData("Повернутися в головне меню", "back_to_menu") }
            });
            await bot.SendTextMessageAsync(chatId, "Чи допомогло це рішення?", replyMarkup: keyboard);
            _databaseService.LogAction(chatId, "Запит на статус вирішення проблеми (1-й етап)");
        }

        private async Task HandleProblemNotResolved2Async(ITelegramBotClient bot, Message message)
        {
            var chatId = message.Chat.Id;
            var keyboard = new InlineKeyboardMarkup(new[]
            {
                new[] { InlineKeyboardButton.WithCallbackData("Так", "problem_resolved") },
                new[] { InlineKeyboardButton.WithCallbackData("Ні", "to_operator") },
                new[] { InlineKeyboardButton.WithCallbackData("Повернутися в головне меню", "back_to_menu") }
            });
            await bot.SendTextMessageAsync(chatId, "Чи допомогло це рішення?", replyMarkup: keyboard);
            _databaseService.LogAction(chatId, "Запит на статус вирішення проблеми (2-й етап)");
        }

        private async Task HandleToOperatorAsync(ITelegramBotClient bot, long chatId, TicketManager ticketManager)
        {
            if (_userData[chatId].MessageIdToEdit == null)
            {
                await bot.SendTextMessageAsync(chatId, "Помилка: повідомлення для редагування не знайдено.");
                _databaseService.LogAction(chatId, "Помилка: повідомлення для редагування не знайдено");
                return;
            }

            if (!ticketManager.IsOperatorOnline)
            {
                var noOperatorKeyboard = new InlineKeyboardMarkup(new[]
                {
                    new[] { InlineKeyboardButton.WithCallbackData("Повторити спробу", "retry_operator") },
                    new[] { InlineKeyboardButton.WithCallbackData("Повернутися в головне меню", "back_to_menu") }
                });
                await bot.EditMessageTextAsync(chatId, _userData[chatId].MessageIdToEdit.Value, "Наразі оператор офлайн. Спробуйте пізніше або скористайтеся іншими опціями.", replyMarkup: noOperatorKeyboard);
                _databaseService.LogAction(chatId, "Спроба звернення до оператора, але оператор офлайн");
                return;
            }

            _userData[chatId].TicketId = ticketManager.GenerateTicketId();
            _databaseService.SaveTicket(chatId, _userData[chatId].TicketId, _userData[chatId].ProblemType, _userData[chatId].ProblemDetails, "unanswered");
            await ticketManager.NotifyAdminAsync(bot, chatId, _userData[chatId]);
            var operatorKeyboard = new InlineKeyboardMarkup(new[]
            {
                new[] { InlineKeyboardButton.WithCallbackData("Повернутися в головне меню", "back_to_menu") }
            });
            await bot.EditMessageTextAsync(chatId, _userData[chatId].MessageIdToEdit.Value, "Ви в черзі на з’єднання з оператором. Очікуйте, будь ласка.", replyMarkup: operatorKeyboard);
            _databaseService.LogAction(chatId, "Користувач у черзі на оператора");
            await Task.Delay(5000);
            _userStates[chatId] = UserState.OperatorConnected;
            var connectedKeyboard = new InlineKeyboardMarkup(new[]
            {
                new[] { InlineKeyboardButton.WithCallbackData("Закрити заявку", "close_ticket") },
                new[] { InlineKeyboardButton.WithCallbackData("Повернутися в головне меню", "back_to_menu") }
            });
            await bot.SendTextMessageAsync(chatId, "Оператор підключений. Опишіть вашу проблему оператору:", replyMarkup: connectedKeyboard);
            _databaseService.LogAction(chatId, $"Створено заявку: {_userData[chatId].TicketId}");
        }

        private async Task HandleRetryOperatorAsync(ITelegramBotClient bot, long chatId, TicketManager ticketManager)
        {
            if (_userData[chatId].MessageIdToEdit == null)
            {
                await bot.SendTextMessageAsync(chatId, "Помилка: повідомлення для редагування не знайдено.");
                _databaseService.LogAction(chatId, "Помилка: повідомлення для редагування не знайдено");
                return;
            }

            if (!ticketManager.IsOperatorOnline)
            {
                var retryNoOperatorKeyboard = new InlineKeyboardMarkup(new[]
                {
                    new[] { InlineKeyboardButton.WithCallbackData("Повторити спробу", "retry_operator") },
                    new[] { InlineKeyboardButton.WithCallbackData("Повернутися в головне меню", "back_to_menu") }
                });
                await bot.EditMessageTextAsync(chatId, _userData[chatId].MessageIdToEdit.Value, "Оператор все ще офлайн. Спробувати ще раз?", replyMarkup: retryNoOperatorKeyboard);
                _databaseService.LogAction(chatId, "Повторна спроба звернення до оператора, але оператор офлайн");
                return;
            }

            _userData[chatId].TicketId = ticketManager.GenerateTicketId();
            _databaseService.SaveTicket(chatId, _userData[chatId].TicketId, _userData[chatId].ProblemType, _userData[chatId].ProblemDetails, "unanswered");
            await ticketManager.NotifyAdminAsync(bot, chatId, _userData[chatId]);
            var retryOperatorKeyboard = new InlineKeyboardMarkup(new[]
            {
                new[] { InlineKeyboardButton.WithCallbackData("Повернутися в головне меню", "back_to_menu") }
            });
            await bot.EditMessageTextAsync(chatId, _userData[chatId].MessageIdToEdit.Value, "Ви в черзі на з’єднання з оператором. Очікуйте, будь ласка.", replyMarkup: retryOperatorKeyboard);
            _databaseService.LogAction(chatId, "Користувач у черзі на оператора після повторної спроби");
            await Task.Delay(5000);
            _userStates[chatId] = UserState.OperatorConnected;
            var retryConnectedKeyboard = new InlineKeyboardMarkup(new[]
            {
                new[] { InlineKeyboardButton.WithCallbackData("Закрити заявку", "close_ticket") },
                new[] { InlineKeyboardButton.WithCallbackData("Повернутися в головне меню", "back_to_menu") }
            });
            await bot.SendTextMessageAsync(chatId, "Оператор підключений. Опишіть вашу проблему оператору:", replyMarkup: retryConnectedKeyboard);
            _databaseService.LogAction(chatId, $"Створено заявку після повторної спроби: {_userData[chatId].TicketId}");
        }
    }
}