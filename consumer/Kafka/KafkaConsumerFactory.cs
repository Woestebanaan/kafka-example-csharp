using Azure.Core;
using Confluent.Kafka;
using KafkaConsumer.Configuration;

namespace KafkaConsumer.Kafka;

static class KafkaConsumerFactory
{
    public static IConsumer<string, string> Create(
        KafkaOptions options,
        TokenCredential credential,
        string? clientId)
    {
        var securityProtocol = Enum.Parse<SecurityProtocol>(options.Security.SecurityProtocol);
        var useSasl = !options.IsMtls || securityProtocol != SecurityProtocol.Ssl;

        var config = new ConsumerConfig
        {
            BootstrapServers                    = options.BootstrapServers,
            GroupId                             = options.GroupId,
            AutoOffsetReset                     = Enum.Parse<AutoOffsetReset>(options.AutoOffsetReset),
            EnableAutoCommit                    = options.EnableAutoCommit,
            SecurityProtocol                    = securityProtocol,
            SaslMechanism                       = useSasl
                                                    ? Enum.Parse<SaslMechanism>(options.Security.SaslMechanism)
                                                    : null,
            SslEndpointIdentificationAlgorithm = Enum.Parse<SslEndpointIdentificationAlgorithm>(
                                                    options.Ssl.SslEndpointIdentificationAlgorithm),
            SslCaLocation                       = options.Ssl.SslCaLocation,
            SslCertificateLocation              = options.Ssl.SslCertificateLocation,
            SslKeyLocation                      = options.Ssl.SslKeyLocation,
            SslKeyPassword                      = options.Ssl.SslKeyPassword,
            EnableSslCertificateVerification    = !options.Ssl.EnableInsecureSsl,
        };

        var builder = new ConsumerBuilder<string, string>(config);

        if (useSasl)
        {
            builder.SetOAuthBearerTokenRefreshHandler((client, _) =>
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
            });
        }

        return builder.Build();
    }
}
