using Azure.Identity;
using Confluent.Kafka;
using Microsoft.Extensions.Configuration;
using System.Net;
using System.Runtime.InteropServices;

var config = new ConfigurationBuilder()
    .SetBasePath(Directory.GetCurrentDirectory())
    .AddJsonFile("appsettings.json", optional: true)
    .AddEnvironmentVariables()
    .AddUserSecrets<Program>()
    .Build();

var kafka = config.GetSection("Kafka");
var clientId = config["AZURE_CLIENT_ID"];
var tenantId = config["AZURE_TENANT_ID"];
var clientSecret = config["AZURE_CLIENT_SECRET"];

var credential = !string.IsNullOrEmpty(clientSecret)
    ? new ClientSecretCredential(tenantId, clientId, clientSecret)
    : (Azure.Core.TokenCredential)new DefaultAzureCredential();

var isReady = false;
var isHealthy = true;
var lastAlive = DateTime.UtcNow;

var listener = new HttpListener();
listener.Prefixes.Add("http://+:8080/");
listener.Start();
_ = Task.Run(async () =>
{
    while (listener.IsListening)
    {
        try
        {
            var ctx = await listener.GetContextAsync();
            ctx.Response.StatusCode = ctx.Request.Url?.AbsolutePath switch
            {
                "/ready" => isReady ? 200 : 503,
                "/live"  => isHealthy && DateTime.UtcNow - lastAlive < TimeSpan.FromSeconds(60) ? 200 : 503,
                _        => 404
            };
            ctx.Response.Close();
        }
        catch { }
    }
});

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };
using var sigterm = PosixSignalRegistration.Create(PosixSignal.SIGTERM, ctx => { ctx.Cancel = true; cts.Cancel(); });

const int MaxAuthFailures = 3;
const int MaxConsecutiveProduceErrors = 5;

var authFailures = 0;
var fatalError = false;

using var producer = new ProducerBuilder<string, string>(new ProducerConfig
{
    BootstrapServers = kafka["BootstrapServers"],
    SecurityProtocol = Enum.Parse<SecurityProtocol>(kafka["Security:SecurityProtocol"] ?? "SaslSsl"),
    SaslMechanism = Enum.Parse<SaslMechanism>(kafka["Security:SaslMechanism"] ?? "OAuthBearer"),
    SslCaLocation = kafka["Ssl:SslCaLocation"] is { Length: > 0 } ca ? ca : null,
    EnableSslCertificateVerification = !bool.TryParse(kafka["Ssl:EnableInsecureSsl"], out var insecure) || !insecure
    // Debug = "all"
})
.SetOAuthBearerTokenRefreshHandler((client, _) =>
{
    try
    {
        var token = credential.GetToken(new([$"{clientId}/.default"]), default);
        client.OAuthBearerSetToken(token.Token, token.ExpiresOn.ToUnixTimeMilliseconds(), clientId);
        Interlocked.Exchange(ref authFailures, 0);
    }
    catch (Exception ex)
    {
        var failures = Interlocked.Increment(ref authFailures);
        Console.Error.WriteLine($"[AUTH] Failure {failures}/{MaxAuthFailures}: {ex.Message}");
        client.OAuthBearerSetTokenFailure(ex.Message);

        if (failures >= MaxAuthFailures)
        {
            Console.Error.WriteLine("[AUTH] Max failures reached, shutting down.");
            isHealthy = false;
            fatalError = true;
            cts.Cancel();
        }
    }
})
.Build();

var topic = kafka["Topic"] ?? "my-topic";
Console.WriteLine($"Producing to topic: {topic}. Press Ctrl+C to exit.");

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
                Key = kafka["Key"],
                Value = message
            }, cts.Token);

            Console.WriteLine($"[{result.TopicPartitionOffset}] {message}");
            consecutiveProduceErrors = 0;
            isReady = true;
            lastAlive = DateTime.UtcNow;

            await Task.Delay(1000, cts.Token);
        }
        catch (ProduceException<string, string> ex)
        {
            consecutiveProduceErrors++;
            Console.Error.WriteLine($"[PRODUCE] Error {consecutiveProduceErrors}/{MaxConsecutiveProduceErrors}: {ex.Error.Reason}");

            if (consecutiveProduceErrors >= MaxConsecutiveProduceErrors)
            {
                Console.Error.WriteLine("[PRODUCE] Max consecutive errors reached, shutting down.");
                isHealthy = false;
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
listener.Stop();

return fatalError ? 1 : 0;
