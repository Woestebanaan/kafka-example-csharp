using Azure.Identity;
using Confluent.Kafka;
using Microsoft.Extensions.Configuration;

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

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

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
    }
    catch (Exception ex)
    {
        client.OAuthBearerSetTokenFailure(ex.Message);
    }
})
.Build();

var topic = kafka["Topic"] ?? "my-topic";
Console.WriteLine($"Producing to topic: {topic}. Press Ctrl+C to exit.");

try
{
    while (!cts.Token.IsCancellationRequested)
    {
        var message = DateTime.UtcNow.ToString("o");
        var result = await producer.ProduceAsync(topic, new Message<string, string>
        {
            Key = kafka["Key"],
            Value = message
        }, cts.Token);

        Console.WriteLine($"[{result.TopicPartitionOffset}] {message}");
        await Task.Delay(1000, cts.Token);
    }
}
catch (OperationCanceledException) { }
catch (ProduceException<string, string> e)
{
    Console.WriteLine($"Produce error: {e.Error.Reason}");
}

producer.Flush(TimeSpan.FromSeconds(10));
