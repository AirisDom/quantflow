namespace QuantFlow.Orchestrator.Services;

public record TradeProposal(
    string Asset,
    decimal Quantity,
    decimal Price,
    TradeSide Side
);

public enum TradeSide
{
    Buy,
    Sell
}

public record RiskDecision(
    bool IsApproved,
    string Reason
);

public record PortfolioState(
    decimal TotalEquity,
    decimal PeakEquity,
    Dictionary<string, decimal> Positions,
    decimal TotalExposure
);

public interface IRiskManager
{
    RiskDecision EvaluateTrade(TradeProposal proposal, PortfolioState portfolio);
    decimal CalculateDrawdown(decimal currentEquity, decimal peakEquity);
    bool ValidatePositionSize(decimal proposedQuantity, decimal price, decimal totalEquity);
    bool ValidateExposure(decimal currentExposure, decimal proposedTradeValue, decimal totalEquity);
}
