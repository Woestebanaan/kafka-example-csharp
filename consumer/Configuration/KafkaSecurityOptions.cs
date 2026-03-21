namespace KafkaConsumer.Configuration;

sealed class KafkaSecurityOptions
{
    public string SecurityProtocol { get; init; } = "SaslSsl";
    public string SaslMechanism { get; init; } = "OAuthBearer";
}
