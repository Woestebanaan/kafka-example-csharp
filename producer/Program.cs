using Azure.Identity;
using Confluent.Kafka;
using KafkaProducer.Configuration;
using KafkaProducer.Health;
using KafkaProducer.Kafka;
using Microsoft.Extensions.Configuration;
using System.Runtime.InteropServices;

var environment = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT") ?? "Production";

var config = new ConfigurationBuilder()
    .SetBasePath(Directory.GetCurrentDirectory())
    .AddJsonFile("appsettings.json", optional: true)
    .AddJsonFile($"appsettings.{environment}.json", optional: true)
    .AddEnvironmentVariables()
    .AddUserSecrets<Program>()
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

var fatalError = false;

using var producer = KafkaProducerFactory.Create(
    kafkaOptions,
    credential,
    clientId,
    onFatalAuthFailure: () =>
    {
        health.MarkUnhealthy();
        fatalError = true;
        cts.Cancel();
    });

var topic = kafkaOptions.Topic;
Console.WriteLine($"Producing to topic: {topic}. Press Ctrl+C to exit.");

const int MaxConsecutiveProduceErrors = 5;
var consecutiveProduceErrors = 0;

try
{
    while (!cts.Token.IsCancellationRequested)
    {
        try
        {
            var message = DateTime.UtcNow.ToString("o");
            var result = await producer.ProduceAsync(topic, new Message<string, string>
            {
                Key   = kafkaOptions.Key!,
                Value = message
            }, cts.Token);

            Console.WriteLine($"[{result.TopicPartitionOffset}] {message}");
            consecutiveProduceErrors = 0;
            health.IsReady = true;
            health.MarkAlive();

            await Task.Delay(1000, cts.Token);
        }
        catch (ProduceException<string, string> ex)
        {
            consecutiveProduceErrors++;
            Console.Error.WriteLine($"[PRODUCE] Error {consecutiveProduceErrors}/{MaxConsecutiveProduceErrors}: {ex.Error.Reason}");

            if (consecutiveProduceErrors >= MaxConsecutiveProduceErrors)
            {
                Console.Error.WriteLine("[PRODUCE] Max consecutive errors reached, shutting down.");
                health.MarkUnhealthy();
                fatalError = true;
                cts.Cancel();
                break;
            }

            // Exponential backoff: 1s, 2s, 4s, 8s, 16s (capped at 30s)
            var delay = TimeSpan.FromSeconds(Math.Min(30, Math.Pow(2, consecutiveProduceErrors - 1)));
            Console.Error.WriteLine($"[PRODUCE] Retrying in {delay.TotalSeconds}s...");
            await Task.Delay(delay, cts.Token);
        }
    }
}
catch (OperationCanceledException) { }

producer.Flush(TimeSpan.FromSeconds(10));

return fatalError ? 1 : 0;
