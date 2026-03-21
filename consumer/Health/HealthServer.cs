using System.Net;

namespace KafkaConsumer.Health;

sealed class HealthServer(string port, HealthState state) : IDisposable
{
    private static readonly TimeSpan LivenessTimeout = TimeSpan.FromSeconds(60);
    private readonly HttpListener _listener = new();

    public void Start()
    {
        _listener.Prefixes.Add($"http://+:{port}/");
        _listener.Start();
        _ = RunAsync();
    }

    private async Task RunAsync()
    {
        while (_listener.IsListening)
        {
            try
            {
                var ctx = await _listener.GetContextAsync();
                ctx.Response.StatusCode = ctx.Request.Url?.AbsolutePath switch
                {
                    "/ready" => state.IsReady ? 200 : 503,
                    "/live"  => state.IsLive(LivenessTimeout) ? 200 : 503,
                    _        => 404
                };
                ctx.Response.Close();
            }
            catch { }
        }
    }

    public void Dispose() => _listener.Stop();
}
