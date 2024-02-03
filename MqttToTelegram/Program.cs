using Telegram.Bot;
using MqttToTelegram;
using Rebus.Config;
using Rebus.Activation;
using Rebus.Transport.InMem;
using Rebus.Routing.TypeBased;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddHostedService<Worker>();
builder.Services.AddHostedService<TelegramWorker>();

var botClient = new TelegramBotClient(File.ReadAllText("Secrets/telegram.token"));

builder.Services.AddSingleton(botClient);

builder.Services.AddRebus(c=>c
    .Transport(t=>t.UseInMemoryTransport(new InMemNetwork(), "Messages"))
    .Routing(r=>r.TypeBased().MapAssemblyOf<Program>("Messages")));

builder.Services.AutoRegisterHandlersFromAssemblyOf<Program>();

var host = builder.Build();
host.Run();
