using Azure.Core;
using Confluent.Kafka;
using KafkaProducer.Configuration;

namespace KafkaProducer.Kafka;

static class KafkaProducerFactory
{
    private const int MaxAuthFailures = 3;

    public static IProducer<string, string> Create(
        KafkaOptions options,
        TokenCredential credential,
        string? clientId,
        Action onFatalAuthFailure)
    {
        var securityProtocol = Enum.Parse<SecurityProtocol>(options.Security.SecurityProtocol);
        var useSasl = !options.IsMtls || securityProtocol != SecurityProtocol.Ssl;

        var builder = new ProducerBuilder<string, string>(new ProducerConfig
        {
            BootstrapServers = options.BootstrapServers,
            SecurityProtocol = securityProtocol,
            SaslMechanism = useSasl
                ? Enum.Parse<SaslMechanism>(options.Security.SaslMechanism)
                : null,
            SslCaLocation            = options.Ssl.SslCaLocation,
            SslCertificateLocation   = options.Ssl.SslCertificateLocation,
            SslKeyLocation           = options.Ssl.SslKeyLocation,
            SslKeyPassword           = options.Ssl.SslKeyPassword,
            EnableSslCertificateVerification = !options.Ssl.EnableInsecureSsl,
        });

        if (useSasl)
        {
            var authFailures = 0;
            builder.SetOAuthBearerTokenRefreshHandler((client, _) =>
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
                        onFatalAuthFailure();
                    }
                }
            });
        }

        return builder.Build();
    }
}
