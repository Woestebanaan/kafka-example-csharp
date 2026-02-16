using Azure.Core;
using Azure.Identity;
using Confluent.Kafka;
using Microsoft.Extensions.Configuration;

var configuration = new ConfigurationBuilder()
    .SetBasePath(Directory.GetCurrentDirectory())
    .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
    .AddEnvironmentVariables()
    .Build();

var kafka = configuration.GetSection("Kafka");

var clientId = Environment.GetEnvironmentVariable("AZURE_CLIENT_ID");
var tenantId = Environment.GetEnvironmentVariable("AZURE_TENANT_ID");
var clientSecret = Environment.GetEnvironmentVariable("AZURE_CLIENT_SECRET");
var scope = $"{clientId}/.default";

TokenCredential credential = (!string.IsNullOrEmpty(tenantId) && !string.IsNullOrEmpty(clientSecret))
    ? new ClientSecretCredential(tenantId, clientId, clientSecret)
    : new DefaultAzureCredential();

var config = new ProducerConfig
{
    BootstrapServers = kafka["BootstrapServers"],
    SecurityProtocol = Enum.Parse<SecurityProtocol>(kafka["Security:SecurityProtocol"] ?? "SaslSsl"),
    SaslMechanism = Enum.Parse<SaslMechanism>(kafka["Security:SaslMechanism"] ?? "OAuthBearer"),
    SslEndpointIdentificationAlgorithm = Enum.Parse<SslEndpointIdentificationAlgorithm>(kafka["Ssl:SslEndpointIdentificationAlgorithm"] ?? "None"),
    SslCaLocation = kafka["Ssl:SslCaLocation"] is { Length: > 0 } ca ? ca : null,
    EnableSslCertificateVerification = !bool.TryParse(kafka["Ssl:EnableInsecureSsl"], out var insecure) || !insecure
};

var topic = kafka["Topic"] ?? "my-topic";

using var producer = new ProducerBuilder<string, string>(config)
    .SetOAuthBearerTokenRefreshHandler((client, _) =>
    {
        try
        {
            var tokenRequestContext = new Azure.Core.TokenRequestContext([scope]);
            var token = credential.GetToken(tokenRequestContext, default);

            client.OAuthBearerSetToken(
                tokenValue: token.Token,
                lifetimeMs: token.ExpiresOn.ToUnixTimeMilliseconds(),
                principalName: clientId);
        }
        catch (Exception ex)
        {
            client.OAuthBearerSetTokenFailure(ex.Message);
        }
    })
    .Build();

using var cts = new CancellationTokenSource();

Console.WriteLine($"Producing to topic: {topic}");
Console.WriteLine("Publishing current time every second... Press Ctrl+C to exit.");

Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

try
{
    while (!cts.Token.IsCancellationRequested)
    {
        var message = DateTime.UtcNow.ToString("o");
        try
        {
            var result = await producer.ProduceAsync(topic, new Message<string, string>
            {
                Key = Environment.MachineName,
                Value = message
            }, cts.Token);

            Console.WriteLine($"Produced message to {result.TopicPartitionOffset}: {message}");
        }
        catch (ProduceException<string, string> e)
        {
            Console.WriteLine($"Produce error: {e.Error.Reason}");
        }

        await Task.Delay(1000, cts.Token);
    }
}
catch (OperationCanceledException)
{
    Console.WriteLine("\nClosing producer...");
}

producer.Flush(TimeSpan.FromSeconds(10));
Console.WriteLine("Producer closed.");
