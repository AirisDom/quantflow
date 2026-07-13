namespace QuantFlow.Orchestrator.Services;

public record TradingStatus(
    bool IsTradingEnabled,
    DateTime? PausedAt,
    string? PausedBy,
    string? PauseReason
);

public interface ITradingControlService
{
    TradingStatus GetStatus();
    TradingStatus Pause(string? reason = null, string? pausedBy = null);
    TradingStatus Resume(string? resumedBy = null);
    bool IsTradingEnabled { get; }
}
