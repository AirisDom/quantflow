using Microsoft.Extensions.Options;
using QuantFlow.Orchestrator.Configuration;

namespace QuantFlow.Orchestrator.Services;

public class RiskManager : IRiskManager
{
    private readonly object _lock = new();
    private decimal _maxDrawdownPercent;
    private decimal _maxPositionSizePercent;
    private decimal _maxExposurePercent;
    private decimal _minOrderValue;

    public RiskManager(IOptions<RiskSettings> options)
    {
        var settings = options.Value;
        _maxDrawdownPercent = settings.MaxDrawdownPercent;
        _maxPositionSizePercent = settings.MaxPositionSizePercent;
        _maxExposurePercent = settings.MaxExposurePercent;
        _minOrderValue = settings.MinOrderValue;
    }

    public RiskDecision EvaluateTrade(TradeProposal proposal, PortfolioState portfolio)
    {
        RiskLimits limits;
        lock (_lock)
        {
            limits = new RiskLimits(_maxDrawdownPercent, _maxPositionSizePercent, _maxExposurePercent, _minOrderValue);
        }

        if (proposal.Quantity <= 0)
            return new RiskDecision(false, "Invalid quantity: must be greater than zero");

        if (proposal.Price <= 0)
            return new RiskDecision(false, "Invalid price: must be greater than zero");

        var tradeValue = proposal.Quantity * proposal.Price;

        if (tradeValue < limits.MinOrderValue)
            return new RiskDecision(false, $"Order value {tradeValue:C} below minimum {limits.MinOrderValue:C}");

        var currentDrawdown = CalculateDrawdown(portfolio.TotalEquity, portfolio.PeakEquity);
        if (currentDrawdown >= limits.MaxDrawdownPercent)
            return new RiskDecision(false, $"Max drawdown limit reached: {currentDrawdown:P2} >= {limits.MaxDrawdownPercent:P2}");

        if (!ValidatePositionSize(proposal.Quantity, proposal.Price, portfolio.TotalEquity))
        {
            var maxAllowed = portfolio.TotalEquity * limits.MaxPositionSizePercent;
            return new RiskDecision(false, $"Position size {tradeValue:C} exceeds limit of {maxAllowed:C} ({limits.MaxPositionSizePercent:P0} of equity)");
        }

        if (proposal.Side == TradeSide.Buy)
        {
            if (!ValidateExposure(portfolio.TotalExposure, tradeValue, portfolio.TotalEquity))
            {
                var maxExposure = portfolio.TotalEquity * limits.MaxExposurePercent;
                return new RiskDecision(false, $"Total exposure would exceed limit of {maxExposure:C} ({limits.MaxExposurePercent:P0} of equity)");
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

        decimal maxPositionSizePercent;
        lock (_lock)
        {
            maxPositionSizePercent = _maxPositionSizePercent;
        }

        var positionValue = proposedQuantity * price;
        var maxPositionValue = totalEquity * maxPositionSizePercent;

        return positionValue <= maxPositionValue;
    }

    public bool ValidateExposure(decimal currentExposure, decimal proposedTradeValue, decimal totalEquity)
    {
        if (totalEquity <= 0)
            return false;

        decimal maxExposurePercent;
        lock (_lock)
        {
            maxExposurePercent = _maxExposurePercent;
        }

        var projectedExposure = currentExposure + proposedTradeValue;
        var maxExposure = totalEquity * maxExposurePercent;

        return projectedExposure <= maxExposure;
    }

    public RiskLimits GetCurrentLimits()
    {
        lock (_lock)
        {
            return new RiskLimits(_maxDrawdownPercent, _maxPositionSizePercent, _maxExposurePercent, _minOrderValue);
        }
    }

    public RiskLimits UpdateLimits(RiskLimitsUpdateRequest request)
    {
        lock (_lock)
        {
            if (request.MaxDrawdownPercent.HasValue)
                _maxDrawdownPercent = request.MaxDrawdownPercent.Value;

            if (request.MaxPositionSizePercent.HasValue)
                _maxPositionSizePercent = request.MaxPositionSizePercent.Value;

            if (request.MaxExposurePercent.HasValue)
                _maxExposurePercent = request.MaxExposurePercent.Value;

            if (request.MinOrderValue.HasValue)
                _minOrderValue = request.MinOrderValue.Value;

            return new RiskLimits(_maxDrawdownPercent, _maxPositionSizePercent, _maxExposurePercent, _minOrderValue);
        }
    }
}
