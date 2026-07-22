namespace QuantFlow.Orchestrator.Shutdown;

public interface IGracefulShutdownService
{
    bool IsShuttingDown { get; }
    int ActiveRequestCount { get; }
    void BeginShutdown();
    void IncrementActiveRequests();
    void DecrementActiveRequests();
    Task WaitForDrainAsync(TimeSpan timeout, CancellationToken cancellationToken);
}

public class GracefulShutdownService : IGracefulShutdownService
{
    private readonly ILogger<GracefulShutdownService> _logger;
    private volatile bool _isShuttingDown;
    private int _activeRequestCount;

    public bool IsShuttingDown => _isShuttingDown;
    public int ActiveRequestCount => _activeRequestCount;

    public GracefulShutdownService(ILogger<GracefulShutdownService> logger)
    {
        _logger = logger;
    }

    public void BeginShutdown()
    {
        _isShuttingDown = true;
        _logger.LogInformation("Graceful shutdown initiated, active requests: {ActiveRequests}", _activeRequestCount);
    }

    public void IncrementActiveRequests()
    {
        Interlocked.Increment(ref _activeRequestCount);
    }

    public void DecrementActiveRequests()
    {
        Interlocked.Decrement(ref _activeRequestCount);
    }

    public async Task WaitForDrainAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + timeout;
        var checkInterval = TimeSpan.FromMilliseconds(100);

        _logger.LogInformation("Waiting for {ActiveRequests} active requests to drain (timeout: {Timeout}s)",
            _activeRequestCount, timeout.TotalSeconds);

        while (_activeRequestCount > 0 && DateTime.UtcNow < deadline && !cancellationToken.IsCancellationRequested)
        {
            await Task.Delay(checkInterval, cancellationToken);
        }

        if (_activeRequestCount > 0)
        {
            _logger.LogWarning("Drain timeout reached with {ActiveRequests} requests still active", _activeRequestCount);
        }
        else
        {
            _logger.LogInformation("All active requests drained successfully");
        }
    }
}
