namespace KafkaConsumer.Configuration;

sealed class KafkaOptions
{
    public string BootstrapServers { get; init; } = "";
    public string Topic { get; init; } = "my-topic";
    public string GroupId { get; init; } = "my-group";
    public string AutoOffsetReset { get; init; } = "Earliest";
    public bool EnableAutoCommit { get; init; } = true;
    public KafkaSecurityOptions Security { get; init; } = new();
    public KafkaSslOptions Ssl { get; init; } = new();

    public bool IsMtls =>
        !string.IsNullOrEmpty(Ssl.SslCertificateLocation) ||
        !string.IsNullOrEmpty(Ssl.SslKeyLocation);
}
