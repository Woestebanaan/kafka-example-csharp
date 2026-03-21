using Azure.Identity;
using Confluent.Kafka;
using KafkaConsumer.Configuration;
using KafkaConsumer.Health;
using KafkaConsumer.Kafka;
using Microsoft.Extensions.Configuration;
using System.Runtime.InteropServices;

var environment = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT") ?? "Production";

var config = new ConfigurationBuilder()
    .SetBasePath(Directory.GetCurrentDirectory())
    .AddJsonFile("appsettings.json", optional: true)
    .AddJsonFile($"appsettings.{environment}.json", optional: true)
    .AddEnvironmentVariables()
    .Build();

var kafkaOptions = config.GetSection("Kafka").Get<KafkaOptions>() ?? new KafkaOptions();

var clientId     = config["AZURE_CLIENT_ID"];
var tenantId     = config["AZURE_TENANT_ID"];
var clientSecret = config["AZURE_CLIENT_SECRET"];

var credential = !string.IsNullOrEmpty(clientSecret)
    ? new ClientSecretCredential(tenantId, clientId, clientSecret)
    : (Azure.Core.TokenCredential)new DefaultAzureCredential();

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };
using var sigterm = PosixSignalRegistration.Create(PosixSignal.SIGTERM, ctx => { ctx.Cancel = true; cts.Cancel(); });

var health = new HealthState();
using var healthServer = new HealthServer(config["HealthPort"] ?? "8080", health);
healthServer.Start();

using var consumer = KafkaConsumerFactory.Create(kafkaOptions, credential, clientId);

consumer.Subscribe(kafkaOptions.Topic);
Console.WriteLine($"Subscribed to topic: {kafkaOptions.Topic}");
Console.WriteLine("Waiting for messages... Press Ctrl+C to exit.");

try
{
    while (!cts.Token.IsCancellationRequested)
    {
        try
        {
            var result = consumer.Consume(cts.Token);
            if (result is { IsPartitionEOF: false })
            {
                Console.WriteLine($"""
                    Received message at {result.TopicPartitionOffset}:
                      Key:       {result.Message.Key}
                      Value:     {result.Message.Value}
                      Timestamp: {result.Message.Timestamp.UtcDateTime}
                    """);
                health.IsReady = true;
                health.MarkAlive();
            }
        }
        catch (ConsumeException ex)
        {
            Console.Error.WriteLine($"[CONSUME] Error: {ex.Error.Reason}");
        }
    }
}
catch (OperationCanceledException)
{
    Console.WriteLine("\nClosing consumer...");
}

consumer.Close();
Console.WriteLine("Consumer closed.");
