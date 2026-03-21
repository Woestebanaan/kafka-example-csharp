namespace KafkaConsumer.Health;

sealed class HealthState
{
    private volatile bool _isReady;
    private volatile bool _isHealthy = true;
    private DateTime _lastAlive = DateTime.UtcNow;

    public bool IsReady
    {
        get => _isReady;
        set => _isReady = value;
    }

    public bool IsHealthy => _isHealthy;

    public void MarkUnhealthy() => _isHealthy = false;

    public void MarkAlive() => _lastAlive = DateTime.UtcNow;

    public bool IsLive(TimeSpan timeout) =>
        _isHealthy && DateTime.UtcNow - _lastAlive < timeout;
}
