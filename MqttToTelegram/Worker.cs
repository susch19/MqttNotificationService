using MQTTnet.Client;
using MQTTnet;
using MQTTnet.Server;
using Telegram.Bot;
using Rebus.Bus;
using System.Text.Json.Serialization;

namespace MqttToTelegram;

public class Worker : BackgroundService
{
    private readonly ILogger<Worker> _logger;
    private readonly TelegramBotClient telegramBot;
    private readonly IConfiguration configuration;
    private readonly IBus bus;
    private readonly MqttFactory mqttFactory;
    private readonly IMqttClient mqttClient;
    private readonly MqttClientOptions mqttClientOptions;

    public Worker(ILogger<Worker> logger, TelegramBotClient telegram, IConfiguration configuration, IBus bus)
    {
        _logger = logger;

        telegramBot = telegram;
        this.configuration = configuration;
        this.bus = bus;
        var settings = configuration.GetSection("Mqtt").Get<MqttSettings>();
        mqttFactory = new MqttFactory();

        mqttClient = mqttFactory.CreateMqttClient();

        mqttClientOptions = new MqttClientOptionsBuilder().WithTcpServer(settings.Server, settings.Port).WithCredentials(settings.RemoteUsername, settings.RemotePassword).Build();

        mqttClient.ApplicationMessageReceivedAsync += MqttClient_ApplicationMessageReceivedAsync;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (!await mqttClient.TryPingAsync())
                {
                    await mqttClient.ConnectAsync(mqttClientOptions, stoppingToken);

                    Console.WriteLine("The MQTT client is connected.");
                    var mqttSubscribeOptions = mqttFactory.CreateSubscribeOptionsBuilder()
                        .WithTopicFilter(f => f.WithTopic("zigbee2mqtt/Türklingel"))
                        .WithTopicFilter(f => f.WithTopic("painless2mqtt/0x000000002d8909fe/state"))
                    .Build();

                    await mqttClient.SubscribeAsync(mqttSubscribeOptions, stoppingToken);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.ToString());
            }
            finally
            {
                await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
            }
        }
    }



    async Task MqttClient_ApplicationMessageReceivedAsync(MqttApplicationMessageReceivedEventArgs e)
    {
        var payload = e.ApplicationMessage.ConvertPayloadToString();

        if (e.ApplicationMessage.Topic == "zigbee2mqtt/Türklingel")
        {
            var obj = System.Text.Json.JsonSerializer.Deserialize<DoorbellObject>(payload)!;
    
            bus.Send(obj);

        }
        else if (e.ApplicationMessage.Topic == "painless2mqtt/0x000000002d8909fe/state")
        {
            var obj = System.Text.Json.JsonSerializer.Deserialize<LedStripState>(payload)!;

            bus.Send(obj);

        }
    }




    public class MqttSettings
    {
        public string Server { get; set; }
        public int Port { get; set; }
        public string RemoteUsername { get; set; }
        public string RemotePassword { get; set; }
    }


}

public record DoorbellObject([property:JsonPropertyName("action")]string Action);

public record LedStripState(string iP, int firmwareVersionNr, bool isConnected, string colorMode, int delay, int numberOfLeds, int brightness, int step, long colorNumber, int version, bool reverse, DateTime lastReceived);
