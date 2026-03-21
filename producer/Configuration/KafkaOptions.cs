namespace KafkaProducer.Configuration;

sealed class KafkaOptions
{
    public string BootstrapServers { get; init; } = "";
    public string Topic { get; init; } = "my-topic";
    public string? Key { get; init; }
    public KafkaSecurityOptions Security { get; init; } = new();
    public KafkaSslOptions Ssl { get; init; } = new();

    public bool IsMtls =>
        !string.IsNullOrEmpty(Ssl.SslCertificateLocation) ||
        !string.IsNullOrEmpty(Ssl.SslKeyLocation);
}
