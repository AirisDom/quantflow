namespace QuantFlow.Orchestrator.Services;

public class RiskManager : IRiskManager
{
    private const decimal MaxDrawdownPercent = 0.05m;
    private const decimal MaxPositionSizePercent = 0.10m;
    private const decimal MaxExposurePercent = 0.80m;
    private const decimal MinOrderValue = 10m;

    public RiskDecision EvaluateTrade(TradeProposal proposal, PortfolioState portfolio)
    {
        if (proposal.Quantity <= 0)
            return new RiskDecision(false, "Invalid quantity: must be greater than zero");

        if (proposal.Price <= 0)
            return new RiskDecision(false, "Invalid price: must be greater than zero");

        var tradeValue = proposal.Quantity * proposal.Price;

        if (tradeValue < MinOrderValue)
            return new RiskDecision(false, $"Order value {tradeValue:C} below minimum {MinOrderValue:C}");

        var currentDrawdown = CalculateDrawdown(portfolio.TotalEquity, portfolio.PeakEquity);
        if (currentDrawdown >= MaxDrawdownPercent)
            return new RiskDecision(false, $"Max drawdown limit reached: {currentDrawdown:P2} >= {MaxDrawdownPercent:P2}");

        if (!ValidatePositionSize(proposal.Quantity, proposal.Price, portfolio.TotalEquity))
        {
            var maxAllowed = portfolio.TotalEquity * MaxPositionSizePercent;
            return new RiskDecision(false, $"Position size {tradeValue:C} exceeds limit of {maxAllowed:C} ({MaxPositionSizePercent:P0} of equity)");
        }

        if (proposal.Side == TradeSide.Buy)
        {
            if (!ValidateExposure(portfolio.TotalExposure, tradeValue, portfolio.TotalEquity))
            {
                var maxExposure = portfolio.TotalEquity * MaxExposurePercent;
                return new RiskDecision(false, $"Total exposure would exceed limit of {maxExposure:C} ({MaxExposurePercent:P0} of equity)");
            }
        }

        var existingPosition = portfolio.Positions.GetValueOrDefault(proposal.Asset, 0m);
        if (proposal.Side == TradeSide.Sell && existingPosition < proposal.Quantity)
            return new RiskDecision(false, $"Insufficient position: attempting to sell {proposal.Quantity} but only hold {existingPosition}");

        return new RiskDecision(true, "Trade approved: all risk checks passed");
    }

    public decimal CalculateDrawdown(decimal currentEquity, decimal peakEquity)
    {
        if (peakEquity <= 0)
            return 0m;

        if (currentEquity >= peakEquity)
            return 0m;

        return (peakEquity - currentEquity) / peakEquity;
    }

    public bool ValidatePositionSize(decimal proposedQuantity, decimal price, decimal totalEquity)
    {
        if (totalEquity <= 0)
            return false;

        var positionValue = proposedQuantity * price;
        var maxPositionValue = totalEquity * MaxPositionSizePercent;

        return positionValue <= maxPositionValue;
    }

    public bool ValidateExposure(decimal currentExposure, decimal proposedTradeValue, decimal totalEquity)
    {
        if (totalEquity <= 0)
            return false;

        var projectedExposure = currentExposure + proposedTradeValue;
        var maxExposure = totalEquity * MaxExposurePercent;

        return projectedExposure <= maxExposure;
    }
}
