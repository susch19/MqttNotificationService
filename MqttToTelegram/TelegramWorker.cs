
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using System;

using File = System.IO.File;
using Rebus.Handlers;
using Telegram.Bot.Types.ReplyMarkups;
using Message = Telegram.Bot.Types.Message;

namespace MqttToTelegram;

public class TelegramWorker : BackgroundService, IHandleMessages<DoorbellObject>, IHandleMessages<LedStripState>
{
    private readonly ILogger<Worker> _logger;
    private readonly TelegramBotClient bot;
    private int offset = 0;
    private Random random = new();
    private Dictionary<long, (int, DateTimeOffset)> registerKeys = new();
    List<UserSettings> userSettings = new();
    public TelegramWorker(ILogger<Worker> logger, TelegramBotClient telegram)
    {
        _logger = logger;

        bot = telegram;


        Directory.CreateDirectory("db");
        LoadEntries();

    }

    private void LoadEntries()
    {
        userSettings.Clear();
        foreach (var item in Directory.EnumerateFiles("db"))
        {
            var res = Newtonsoft.Json.JsonConvert.DeserializeObject<UserSettings>(File.ReadAllText(item));
            if (res is null)
                continue;
            userSettings.Add(res);
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var updates = await bot.GetUpdatesAsync(offset, allowedUpdates: [UpdateType.Message, UpdateType.CallbackQuery], timeout: 3600);

            foreach (var update in updates)
            {
                offset = update.Id + 1;
                try
                {

                    var chatId = update.Type switch
                    {
                        UpdateType.Message => update.Message.From.Id,
                        UpdateType.CallbackQuery => update.CallbackQuery.From.Id,
                        _ => -1
                    };


                    var settings = userSettings.FirstOrDefault(x => x.UserId == chatId);
                    if (update.Type == UpdateType.Message && update.Message is not null)
                        await ProcessMessage(update.Message!, settings);
                    if (update.Type == UpdateType.CallbackQuery)
                        await ProcessUpdateQuery(update.CallbackQuery!, settings);

                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Telegram Message could not be processed");
                }
            }

        }
    }

    private async Task ProcessUpdateQuery(CallbackQuery callbackQuery, UserSettings? settings)
    {
        var command = callbackQuery.Data[0];
        switch (command)
        {
            case 'n':
                {
                    if (settings is null)
                        return;

                    var nt = callbackQuery.Data[1];
                    if (nt == 'k')
                    {
                        settings.ReceiveDoorbellNotifications = callbackQuery.Data[2] == '1';
                    }
                    else if (nt == 'e')
                    {
                        settings.ReceiveDinnersReadyNotifications = callbackQuery.Data[2] == '1';
                    }
                    settings.Save();
                    await bot.AnswerCallbackQueryAsync(callbackQuery.Id);
                    //bot.EditMessageReplyMarkupAsync()
                    SendNotificationKeyboard(settings, callbackQuery.Message.MessageId);
                    break;
                }
            default:
                break;
        }
    }

    private async Task ProcessMessage(Telegram.Bot.Types.Message message, UserSettings? settings)
    {
        if (message.Type != MessageType.Text || string.IsNullOrWhiteSpace(message.Text))
            return;


        if (message.Text.StartsWith('/'))
        {
            var splitted = message.Text[1..].Split(' ');
            switch (splitted[0])
            {
                case "start":
                    {

                        var secondFactor = random.Next(100000, 999999);
                        var filePath = $"db/{message.From.Id}.json";
                        if (settings is null)
                        {
                            settings = new() { FirstName = message.From.FirstName, RegistrationCode = secondFactor, UserId = message.From.Id, UserName = message.From.Username, ChatId = message.Chat.Id };
                            userSettings.Add(settings);
                        }
                        settings.Save();


                        registerKeys[message.From.Id] = (secondFactor, DateTimeOffset.Now);
                        bot.SendTextMessageAsync(message.Chat.Id, "Bitte gib den Code ein, welcher dir vom Admin zur Verfügung gestellt wurde.");

                        var adminUser = userSettings.FirstOrDefault(x => x.IsAdmin);
                        if (adminUser is not null)
                        {
                            bot.SendTextMessageAsync(adminUser.ChatId, $"User: {message.From.FirstName}, {message.From.LastName}, {message.From.Username}\nCode: {secondFactor}");
                        }
                        break;
                    }
                case "notify":
                    {
                        if (settings is null)
                            return;
                        if (splitted.Length < 2)
                        {
                            var kb = await SendNotificationKeyboard(settings);
                            //var bellInline = new InlineQueryResultArticle("bell", $"Klingel {(settings.ReceiveDoorbellNotifications ? "" : "")}", new InputTextMessageContent("/notify "));

                            //bot.AnswerInlineQueryAsync("", [new InlineQueryResultArticle("bell", "Klingel ", new InputTextMessageContent()]);
                            return;
                        }

                        bot.SendTextMessageAsync(
                            chatId: settings.ChatId,
                            text: "Removing keyboard",
                            replyMarkup: new ReplyKeyboardRemove());

                        if (splitted[1].Equals("Klingel", StringComparison.OrdinalIgnoreCase))
                        {
                            if (splitted.Length == 3 && bool.TryParse(splitted[2], out var b))
                                settings.ReceiveDoorbellNotifications = b;
                            else
                                settings.ReceiveDoorbellNotifications = !settings.ReceiveDoorbellNotifications;
                            settings.Save();
                            bot.SendTextMessageAsync(settings.ChatId, $"Du erhälst nun {(settings.ReceiveDoorbellNotifications ? "" : "keine")} Benachrichtungen für die Klingel");
                        }
                        else if (splitted[1].Equals("Essen", StringComparison.OrdinalIgnoreCase))
                        {
                            if (splitted.Length == 3 && bool.TryParse(splitted[2], out var b))
                                settings.ReceiveDinnersReadyNotifications = b;
                            else
                                settings.ReceiveDinnersReadyNotifications = !settings.ReceiveDinnersReadyNotifications;
                            settings.Save();
                            bot.SendTextMessageAsync(settings.ChatId, $"Du erhälst nun {(settings.ReceiveDinnersReadyNotifications ? "" : "keine")} Benachrichtungen für fertiges Essen");
                        }
                    }
                    break;
                case "reload":
                    {
                        if (settings is null || !settings.IsAdmin)
                            return;
                        LoadEntries();
                        bot.SendTextMessageAsync(settings.ChatId, $"Config Reload wurde angestoßen");
                        break;
                    }
                default:
                    break;
            }
        }
        if (message.Text.Length == 6 && int.TryParse(message.Text, out var code))
        {
            var setting = userSettings.FirstOrDefault(x => x.UserId == message.From.Id);
            if (setting is null)
                return;
            if (setting.RegistrationCode < 0)
                bot.SendTextMessageAsync(message.Chat.Id, "Sie sind bereits authorisiert");
            else if (setting.RegistrationCode == code)
            {
                setting.RegistrationCode = -1;
                setting.Save();
                bot.SendTextMessageAsync(message.Chat.Id, "Erfolgreich authorisiert");

            }
            if (setting.IsAdmin)
                bot.SetMyCommandsAsync([new BotCommand { Command = "reload" }], new BotCommandScopeChat() { ChatId = setting.ChatId });
        }
    }

    private async Task<Message> SendNotificationKeyboard(UserSettings? settings, int? editId = null)
    {
        InlineKeyboardMarkup inlineKeyboard = new(new[]
        {
                                // first row
                                new[]
                                {

                                    InlineKeyboardButton.WithCallbackData($"Klingel {(settings.ReceiveDoorbellNotifications ? "aus" : "an")}", $"nk{(settings.ReceiveDoorbellNotifications ? "0" : "1")}"),
                                    InlineKeyboardButton.WithCallbackData($"Essen {(settings.ReceiveDinnersReadyNotifications ? "aus" : "an")}", $"ne{(settings.ReceiveDinnersReadyNotifications ? "0" : "1")}"),
                                }

                            });
        var text =
            $"""
            Welche Benachrichtung möchtest du ändern?
            Klingel: {(settings.ReceiveDoorbellNotifications ? "angeschaltet" : "ausgeschaltet")}
            Essen: {(settings.ReceiveDinnersReadyNotifications ? "angeschaltet" : "ausgeschaltet")}
            """;
        if (editId is null)
        {
            return await bot.SendTextMessageAsync(chatId: settings.ChatId,
                text: text,
                replyMarkup: inlineKeyboard
                );

        }
        else
        {
            return await bot.EditMessageTextAsync(chatId: settings.ChatId, messageId: editId.Value,
            text: text,
            replyMarkup: inlineKeyboard);

        }
    }

    public async Task Handle(DoorbellObject message)
    {
        if (message.Action == "pressed")
        {
            foreach (var item in userSettings.Where(x => x.ReceiveDoorbellNotifications))
            {
                await bot.SendTextMessageAsync(item.ChatId, "Es hat geklingelt");
            }
        }
    }

    public async Task Handle(LedStripState message)
    {
        if (message.colorMode != "Mode")
            return;

        foreach (var item in userSettings.Where(x => x.ReceiveDinnersReadyNotifications))
        {
            await bot.SendTextMessageAsync(item.ChatId, "Essen ist fertig");
        }
    }


    public class UserSettings
    {
        public long UserId { get; set; }
        public long ChatId { get; set; }
        public string FirstName { get; set; }
        public string? UserName { get; set; }
        public int RegistrationCode { get; set; }
        public bool ReceiveDoorbellNotifications { get; set; }
        public bool ReceiveDinnersReadyNotifications { get; set; }
        public bool IsAdmin { get; set; } = false;

        internal void Save()
        {

            var filePath = $"db/{UserId}.json";
            File.WriteAllText(filePath, Newtonsoft.Json.JsonConvert.SerializeObject(this));
        }
    }
}
