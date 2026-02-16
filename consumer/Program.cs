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

var config = new ConsumerConfig
{
    BootstrapServers = kafka["BootstrapServers"],
    GroupId = kafka["GroupId"],
    AutoOffsetReset = Enum.Parse<AutoOffsetReset>(kafka["AutoOffsetReset"] ?? "Earliest"),
    EnableAutoCommit = bool.Parse(kafka["EnableAutoCommit"] ?? "true"),
    SecurityProtocol = Enum.Parse<SecurityProtocol>(kafka["Security:SecurityProtocol"] ?? "SaslSsl"),
    SaslMechanism = Enum.Parse<SaslMechanism>(kafka["Security:SaslMechanism"] ?? "OAuthBearer"),
    SslEndpointIdentificationAlgorithm = Enum.Parse<SslEndpointIdentificationAlgorithm>(kafka["Ssl:SslEndpointIdentificationAlgorithm"] ?? "None"),
    SslCaLocation = kafka["Ssl:SslCaLocation"] is { Length: > 0 } ca ? ca : null,
    EnableSslCertificateVerification = !bool.TryParse(kafka["Ssl:EnableInsecureSsl"], out var insecure) || !insecure
};

var topic = kafka["Topic"] ?? "my-topic";

using var consumer = new ConsumerBuilder<string, string>(config)
    .SetOAuthBearerTokenRefreshHandler((client, _) =>
    {
        try
        {
            // Request token using federated credentials
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

consumer.Subscribe(topic);
Console.WriteLine($"Subscribed to topic: {topic}");
Console.WriteLine("Waiting for messages... Press Ctrl+C to exit.");

Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

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
                      Key: {result.Message.Key}
                      Value: {result.Message.Value}
                      Timestamp: {result.Message.Timestamp.UtcDateTime}
                    """);
            }
        }
        catch (ConsumeException e)
        {
            Console.WriteLine($"Consume error: {e.Error.Reason}");
        }
    }
}
catch (OperationCanceledException)
{
    Console.WriteLine("\nClosing consumer...");
}

consumer.Close();
Console.WriteLine("Consumer closed.");
