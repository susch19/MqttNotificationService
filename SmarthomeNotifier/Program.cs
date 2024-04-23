using Microsoft.Toolkit.Uwp.Notifications;

using MQTTnet.Client;
using MQTTnet;
using ABI.Windows.ApplicationModel.Activation;
using System.Text.Json.Serialization;


var mqttFactory = new MqttFactory();

using (var mqttClient = mqttFactory.CreateMqttClient())
{
    var mqttClientOptions = new MqttClientOptionsBuilder().WithTcpServer("192.168.49.123").Build();

    // Setup message handling before connecting so that queued messages
    // are also handled properly. When there is no event handler attached all
    // received messages get lost.
    mqttClient.ApplicationMessageReceivedAsync += e =>
    {
        Console.WriteLine("Received application message.");


        return Task.CompletedTask;
    };

    _ = Task.Run(
                async () =>
                {
                    while (true)
                    {
                        try
                        {
                            if (!await mqttClient.TryPingAsync())
                            {
                                await mqttClient.ConnectAsync(mqttClientOptions, CancellationToken.None);

                                Console.WriteLine("The MQTT client is connected.");
                                var mqttSubscribeOptions = mqttFactory.CreateSubscribeOptionsBuilder()
                                    .WithTopicFilter(f => f.WithTopic("zigbee2mqtt/Türklingel"))
                                    .WithTopicFilter(f => f.WithTopic("painless2mqtt/0x000000002d8909fe/state"))
                                    .Build();

                                await mqttClient.SubscribeAsync(mqttSubscribeOptions, CancellationToken.None);
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine(ex.ToString());
                        }
                        finally
                        {
                            await Task.Delay(TimeSpan.FromSeconds(1));
                        }
                    }
                });

    //var mqttSubscribeOptions = mqttFactory.CreateSubscribeOptionsBuilder()
    //    .WithTopicFilter(f => f.WithTopic("esp/doorbell"))
    //    .WithTopicFilter(f => f.WithTopic("painless2mqtt/0x000000002d8909fe/state"))
    //    .Build();

    //await mqttClient.SubscribeAsync(mqttSubscribeOptions, CancellationToken.None);

    mqttClient.ApplicationMessageReceivedAsync += MqttClient_ApplicationMessageReceivedAsync;


    var builder = Host.CreateApplicationBuilder(args);

    var host = builder.Build();
    host.Run();
}


async Task MqttClient_ApplicationMessageReceivedAsync(MqttApplicationMessageReceivedEventArgs e)
{
    var payload = e.ApplicationMessage.ConvertPayloadToString();
    var asd = 5 + 5;
    if (e.ApplicationMessage.Topic == "zigbee2mqtt/Türklingel")
    {
        var obj = System.Text.Json.JsonSerializer.Deserialize<DoorbellObject>(payload)!;
        if (obj.State)
            new ToastContentBuilder()
                .AddText("Es hat geklingelt")
                .Show();
    }
    else if (e.ApplicationMessage.Topic == "painless2mqtt/0x000000002d8909fe/state")
    {
        var obj = System.Text.Json.JsonSerializer.Deserialize<LedStripState>(payload)!;
        if (obj.colorMode != "Mode")
            return;

        new ToastContentBuilder()
            .AddText("Essen ist fertig")
            .Show();
    }
}


public class DoorbellObject
{
    [JsonPropertyName("action")]
    public string Action { get; set; }
    [JsonPropertyName("state")]
    public bool State { get; set; }
}

public class LedStripState
{
    public string iP { get; set; }
    public int firmwareVersionNr { get; set; }
    public bool isConnected { get; set; }
    public string colorMode { get; set; }
    public int delay { get; set; }
    public int numberOfLeds { get; set; }
    public int brightness { get; set; }
    public int step { get; set; }
    public long colorNumber { get; set; }
    public int version { get; set; }
    public bool reverse { get; set; }
    public DateTime lastReceived { get; set; }
}
