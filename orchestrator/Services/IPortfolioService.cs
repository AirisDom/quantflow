namespace QuantFlow.Orchestrator.Services;

public record Position(
    string Asset,
    decimal Quantity,
    decimal AverageCost,
    decimal CurrentPrice
)
{
    public decimal MarketValue => Quantity * CurrentPrice;
    public decimal CostBasis => Quantity * AverageCost;
    public decimal UnrealizedPnL => MarketValue - CostBasis;
    public decimal UnrealizedPnLPercent => CostBasis > 0 ? (UnrealizedPnL / CostBasis) * 100m : 0m;
}

public record ExecutedTrade(
    string Asset,
    decimal Quantity,
    decimal Price,
    TradeSide Side,
    DateTime ExecutedAt
);

public record PortfolioSummary(
    decimal CashBalance,
    decimal TotalMarketValue,
    decimal TotalEquity,
    decimal RealizedPnL,
    decimal UnrealizedPnL,
    decimal PeakEquity,
    IReadOnlyDictionary<string, Position> Positions
);

public interface IPortfolioService
{
    decimal CashBalance { get; }
    decimal RealizedPnL { get; }
    decimal PeakEquity { get; }

    void UpdatePositionAfterTrade(ExecutedTrade trade);
    void UpdatePrice(string asset, decimal currentPrice);
    decimal GetPortfolioValue();
    Position? GetPosition(string asset);
    IReadOnlyDictionary<string, Position> GetAllPositions();
    PortfolioState GetPortfolioStateForRisk();
    PortfolioSummary GetSummary();
    void Reset(decimal initialCash);
}
