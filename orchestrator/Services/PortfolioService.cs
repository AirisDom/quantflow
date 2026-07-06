namespace QuantFlow.Orchestrator.Services;

public class PortfolioService : IPortfolioService
{
    private readonly object _lock = new();
    private readonly Dictionary<string, Position> _positions = new();
    private decimal _cashBalance;
    private decimal _realizedPnL;
    private decimal _peakEquity;

    public PortfolioService()
    {
        _cashBalance = 100_000m;
        _peakEquity = _cashBalance;
    }

    public decimal CashBalance
    {
        get { lock (_lock) return _cashBalance; }
    }

    public decimal RealizedPnL
    {
        get { lock (_lock) return _realizedPnL; }
    }

    public decimal PeakEquity
    {
        get { lock (_lock) return _peakEquity; }
    }

    public void UpdatePositionAfterTrade(ExecutedTrade trade)
    {
        lock (_lock)
        {
            var tradeValue = trade.Quantity * trade.Price;

            if (trade.Side == TradeSide.Buy)
            {
                ProcessBuy(trade.Asset, trade.Quantity, trade.Price, tradeValue);
            }
            else
            {
                ProcessSell(trade.Asset, trade.Quantity, trade.Price, tradeValue);
            }

            UpdatePeakEquity();
        }
    }

    private void ProcessBuy(string asset, decimal quantity, decimal price, decimal tradeValue)
    {
        _cashBalance -= tradeValue;

        if (_positions.TryGetValue(asset, out var existing))
        {
            var newQuantity = existing.Quantity + quantity;
            var newCostBasis = existing.CostBasis + tradeValue;
            var newAverageCost = newCostBasis / newQuantity;

            _positions[asset] = new Position(
                asset,
                newQuantity,
                newAverageCost,
                price
            );
        }
        else
        {
            _positions[asset] = new Position(
                asset,
                quantity,
                price,
                price
            );
        }
    }

    private void ProcessSell(string asset, decimal quantity, decimal price, decimal tradeValue)
    {
        _cashBalance += tradeValue;

        if (!_positions.TryGetValue(asset, out var existing))
            return;

        var realizedGain = (price - existing.AverageCost) * quantity;
        _realizedPnL += realizedGain;

        var newQuantity = existing.Quantity - quantity;

        if (newQuantity <= 0)
        {
            _positions.Remove(asset);
        }
        else
        {
            _positions[asset] = new Position(
                asset,
                newQuantity,
                existing.AverageCost,
                price
            );
        }
    }

    private void UpdatePeakEquity()
    {
        var currentEquity = CalculateTotalEquity();
        if (currentEquity > _peakEquity)
        {
            _peakEquity = currentEquity;
        }
    }

    public void UpdatePrice(string asset, decimal currentPrice)
    {
        lock (_lock)
        {
            if (_positions.TryGetValue(asset, out var existing))
            {
                _positions[asset] = new Position(
                    asset,
                    existing.Quantity,
                    existing.AverageCost,
                    currentPrice
                );

                UpdatePeakEquity();
            }
        }
    }

    public decimal GetPortfolioValue()
    {
        lock (_lock)
        {
            return CalculateTotalEquity();
        }
    }

    private decimal CalculateTotalEquity()
    {
        var marketValue = _positions.Values.Sum(p => p.MarketValue);
        return _cashBalance + marketValue;
    }

    private decimal CalculateTotalExposure()
    {
        return _positions.Values.Sum(p => p.MarketValue);
    }

    public Position? GetPosition(string asset)
    {
        lock (_lock)
        {
            return _positions.GetValueOrDefault(asset);
        }
    }

    public IReadOnlyDictionary<string, Position> GetAllPositions()
    {
        lock (_lock)
        {
            return new Dictionary<string, Position>(_positions);
        }
    }

    public PortfolioState GetPortfolioStateForRisk()
    {
        lock (_lock)
        {
            var positionQuantities = _positions
                .ToDictionary(kvp => kvp.Key, kvp => kvp.Value.Quantity);

            return new PortfolioState(
                CalculateTotalEquity(),
                _peakEquity,
                positionQuantities,
                CalculateTotalExposure()
            );
        }
    }

    public PortfolioSummary GetSummary()
    {
        lock (_lock)
        {
            var totalMarketValue = _positions.Values.Sum(p => p.MarketValue);
            var unrealizedPnL = _positions.Values.Sum(p => p.UnrealizedPnL);

            return new PortfolioSummary(
                _cashBalance,
                totalMarketValue,
                CalculateTotalEquity(),
                _realizedPnL,
                unrealizedPnL,
                _peakEquity,
                new Dictionary<string, Position>(_positions)
            );
        }
    }

    public void Reset(decimal initialCash)
    {
        lock (_lock)
        {
            _positions.Clear();
            _cashBalance = initialCash;
            _realizedPnL = 0m;
            _peakEquity = initialCash;
        }
    }
}
