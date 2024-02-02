using MQTTnet.Client;
using MQTTnet;
using MQTTnet.Server;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using System;

using File = System.IO.File;
using Rebus.Handlers;

namespace MqttToTelegram;

public class TelegramWorker : BackgroundService, IHandleMessages<DoorbellObject>, IHandleMessages<LedStripState>
{
    private readonly ILogger<Worker> _logger;
    private readonly TelegramBotClient telegramBot;
    private int offset = 0;
    private Random random = new();
    private Dictionary<long, (int, DateTimeOffset)> registerKeys = new();
    List<UserSettings> userSettings = new();

    public TelegramWorker(ILogger<Worker> logger, TelegramBotClient telegram)
    {
        _logger = logger;

        telegramBot = telegram;

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
            var updates = await telegramBot.GetUpdatesAsync(offset, allowedUpdates: [UpdateType.Message], timeout: 3600);

            foreach (var update in updates)
            {
                offset = update.Id + 1;
                if (update.Type != UpdateType.Message || update.Message is null || update.Message.From.IsBot)
                    continue;
                ProcessMessage(update.Message!);
            }
        }
    }

    private void ProcessMessage(Message message)
    {
        if (message.Type != MessageType.Text || string.IsNullOrWhiteSpace(message.Text))
            return;


        if (message.Text.StartsWith('/'))
        {
            var splitted = message.Text[1..].Split(' ');
            var settings = userSettings.FirstOrDefault(x => x.UserId == message.From.Id);
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
                        telegramBot.SendTextMessageAsync(message.Chat.Id, "Bitte gib den Code ein, welcher dir vom Admin zur Verfügung gestellt wurde.");

                        var adminUser = userSettings.FirstOrDefault(x => x.IsAdmin);
                        if (adminUser is not null)
                        {
                            telegramBot.SendTextMessageAsync(adminUser.ChatId, $"User: {message.From.FirstName}, {message.From.LastName}, {message.From.Username}\nCode: {secondFactor}");
                        }
                        break;
                    }
                case "notify":
                    {
                        if (settings is null || splitted.Length < 2)
                            return;


                        if (splitted[1].Equals("Klingel", StringComparison.OrdinalIgnoreCase))
                        {
                            if (splitted.Length == 3 && bool.TryParse(splitted[2], out var b))
                                settings.ReceiveDoorbellNotifications = b;
                            else
                                settings.ReceiveDoorbellNotifications = !settings.ReceiveDoorbellNotifications;
                            settings.Save();
                            telegramBot.SendTextMessageAsync(settings.ChatId, $"Du erhälst nun {(settings.ReceiveDoorbellNotifications ? "" : "keine")} Benachrichtungen für die Klingel");
                        }
                        else if (splitted[1].Equals("Essen", StringComparison.OrdinalIgnoreCase))
                        {
                            if (splitted.Length == 3 && bool.TryParse(splitted[2], out var b))
                                settings.ReceiveDinnersReadyNotifications = b;
                            else
                                settings.ReceiveDinnersReadyNotifications = !settings.ReceiveDinnersReadyNotifications;
                            settings.Save();
                            telegramBot.SendTextMessageAsync(settings.ChatId, $"Du erhälst nun {(settings.ReceiveDinnersReadyNotifications ? "" : "keine")} Benachrichtungen für fertiges Essen");
                        }
                    }
                    break;
                case "reload":
                    {
                        if (settings is null || !settings.IsAdmin)
                            return;
                        LoadEntries();
                        telegramBot.SendTextMessageAsync(settings.ChatId, $"Config Reload wurde angestoßen");
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
                telegramBot.SendTextMessageAsync(message.Chat.Id, "Sie sind bereits authorisiert");
            else if (setting.RegistrationCode == code)
            {
                setting.RegistrationCode = -1;
                setting.Save();
                telegramBot.SendTextMessageAsync(message.Chat.Id, "Erfolgreich authorisiert");
            }
        }
    }
    public async Task Handle(DoorbellObject message)
    {
        if (message.Action != "ring")
            return;
        foreach (var item in userSettings.Where(x => x.ReceiveDoorbellNotifications))
        {
            await telegramBot.SendTextMessageAsync(item.ChatId, "Es hat geklingelt");
        }
    }

    public async Task Handle(LedStripState message)
    {
        if (message.colorMode != "Mode")
            return;

        foreach (var item in userSettings.Where(x => x.ReceiveDinnersReadyNotifications))
        {
            await telegramBot.SendTextMessageAsync(item.ChatId, "Essen ist fertig");
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
