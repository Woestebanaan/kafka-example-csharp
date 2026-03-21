namespace KafkaConsumer.Configuration;

sealed class KafkaSslOptions
{
    public string? SslCaLocation { get; init; }
    public string? SslCertificateLocation { get; init; }
    public string? SslKeyLocation { get; init; }
    public string? SslKeyPassword { get; init; }
    public bool EnableInsecureSsl { get; init; }
    public string SslEndpointIdentificationAlgorithm { get; init; } = "None";
}
