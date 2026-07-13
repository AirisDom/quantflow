namespace QuantFlow.Orchestrator.Services;

public class TradingControlService : ITradingControlService
{
    private readonly object _lock = new();
    private readonly ILogger<TradingControlService> _logger;
    private bool _isTradingEnabled = true;
    private DateTime? _pausedAt;
    private string? _pausedBy;
    private string? _pauseReason;

    public TradingControlService(ILogger<TradingControlService> logger)
    {
        _logger = logger;
    }

    public bool IsTradingEnabled
    {
        get
        {
            lock (_lock)
            {
                return _isTradingEnabled;
            }
        }
    }

    public TradingStatus GetStatus()
    {
        lock (_lock)
        {
            return new TradingStatus(_isTradingEnabled, _pausedAt, _pausedBy, _pauseReason);
        }
    }

    public TradingStatus Pause(string? reason = null, string? pausedBy = null)
    {
        lock (_lock)
        {
            if (!_isTradingEnabled)
            {
                _logger.LogWarning("Trading already paused, ignoring pause request");
                return new TradingStatus(_isTradingEnabled, _pausedAt, _pausedBy, _pauseReason);
            }

            _isTradingEnabled = false;
            _pausedAt = DateTime.UtcNow;
            _pausedBy = pausedBy;
            _pauseReason = reason;

            _logger.LogWarning("Trading PAUSED by {PausedBy}: {Reason}", pausedBy ?? "system", reason ?? "no reason provided");

            return new TradingStatus(_isTradingEnabled, _pausedAt, _pausedBy, _pauseReason);
        }
    }

    public TradingStatus Resume(string? resumedBy = null)
    {
        lock (_lock)
        {
            if (_isTradingEnabled)
            {
                _logger.LogWarning("Trading already active, ignoring resume request");
                return new TradingStatus(_isTradingEnabled, _pausedAt, _pausedBy, _pauseReason);
            }

            var wasPausedAt = _pausedAt;
            var wasPausedBy = _pausedBy;
            var wasPausedReason = _pauseReason;

            _isTradingEnabled = true;
            _pausedAt = null;
            _pausedBy = null;
            _pauseReason = null;

            _logger.LogInformation("Trading RESUMED by {ResumedBy} (was paused at {PausedAt} by {PausedBy}: {Reason})",
                resumedBy ?? "system", wasPausedAt, wasPausedBy ?? "system", wasPausedReason ?? "no reason");

            return new TradingStatus(_isTradingEnabled, _pausedAt, _pausedBy, _pauseReason);
        }
    }
}
