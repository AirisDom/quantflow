namespace QuantFlow.Orchestrator.Api;

/// <summary>Health check response</summary>
/// <param name="Status">Current health status</param>
/// <param name="Service">Service name</param>
/// <param name="Timestamp">UTC timestamp of the health check</param>
public record HealthResponse(string Status, string Service, DateTime Timestamp);

/// <summary>Readiness check response</summary>
/// <param name="Status">Current readiness status</param>
/// <param name="Service">Service name</param>
/// <param name="Timestamp">UTC timestamp of the readiness check</param>
/// <param name="Checks">Individual dependency check results</param>
public record ReadyResponse(
    string Status,
    string Service,
    DateTime Timestamp,
    IEnumerable<DependencyCheck> Checks
);

/// <summary>Individual dependency check result</summary>
/// <param name="Name">Name of the dependency</param>
/// <param name="Status">Status of the dependency (Healthy/Unhealthy)</param>
/// <param name="DurationMs">Time taken for the check in milliseconds</param>
public record DependencyCheck(string Name, string Status, long DurationMs);

/// <summary>Portfolio position details</summary>
/// <param name="Asset">Asset symbol</param>
/// <param name="Quantity">Quantity held</param>
/// <param name="AverageCost">Average cost per unit</param>
/// <param name="CurrentPrice">Current market price</param>
/// <param name="MarketValue">Total market value</param>
/// <param name="UnrealizedPnL">Unrealized profit/loss</param>
/// <param name="UnrealizedPnLPercent">Unrealized P&amp;L as percentage</param>
public record PositionResponse(
    string Asset,
    decimal Quantity,
    decimal AverageCost,
    decimal CurrentPrice,
    decimal MarketValue,
    decimal UnrealizedPnL,
    decimal UnrealizedPnLPercent
);

/// <summary>Portfolio summary response</summary>
/// <param name="CashBalance">Available cash balance</param>
/// <param name="TotalMarketValue">Total market value of all positions</param>
/// <param name="TotalEquity">Total equity (cash + positions)</param>
/// <param name="RealizedPnL">Realized profit/loss from closed trades</param>
/// <param name="UnrealizedPnL">Unrealized profit/loss from open positions</param>
/// <param name="PeakEquity">Peak equity reached</param>
/// <param name="Positions">List of current positions</param>
public record PortfolioResponse(
    decimal CashBalance,
    decimal TotalMarketValue,
    decimal TotalEquity,
    decimal RealizedPnL,
    decimal UnrealizedPnL,
    decimal PeakEquity,
    IEnumerable<PositionResponse> Positions
);

/// <summary>Trade record details</summary>
/// <param name="Id">Unique trade identifier</param>
/// <param name="OrderId">Order identifier</param>
/// <param name="Asset">Asset symbol</param>
/// <param name="Side">Trade side (Buy/Sell)</param>
/// <param name="Quantity">Quantity traded</param>
/// <param name="Price">Execution price</param>
/// <param name="Timestamp">Execution timestamp</param>
public record TradeResponse(
    Guid Id,
    string OrderId,
    string Asset,
    string Side,
    decimal Quantity,
    decimal Price,
    DateTime Timestamp
);

/// <summary>Paginated trade list response</summary>
/// <param name="Page">Current page number</param>
/// <param name="PageSize">Number of items per page</param>
/// <param name="TotalCount">Total number of trades</param>
/// <param name="TotalPages">Total number of pages</param>
/// <param name="Trades">List of trades on this page</param>
public record TradesPagedResponse(
    int Page,
    int PageSize,
    int TotalCount,
    int TotalPages,
    IEnumerable<TradeResponse> Trades
);

/// <summary>Risk limits configuration</summary>
/// <param name="MaxDrawdownPercent">Maximum drawdown as decimal (0.05 = 5%)</param>
/// <param name="MaxPositionSizePercent">Maximum position size as decimal</param>
/// <param name="MaxExposurePercent">Maximum exposure as decimal</param>
/// <param name="MinOrderValue">Minimum order value</param>
/// <param name="MaxDrawdownPercentFormatted">Max drawdown formatted as percentage</param>
/// <param name="MaxPositionSizePercentFormatted">Max position size formatted as percentage</param>
/// <param name="MaxExposurePercentFormatted">Max exposure formatted as percentage</param>
public record RiskLimitsResponse(
    decimal MaxDrawdownPercent,
    decimal MaxPositionSizePercent,
    decimal MaxExposurePercent,
    decimal MinOrderValue,
    string MaxDrawdownPercentFormatted,
    string MaxPositionSizePercentFormatted,
    string MaxExposurePercentFormatted
);

/// <summary>Risk limits update response</summary>
/// <param name="Message">Success message</param>
/// <param name="Limits">Updated risk limits</param>
public record RiskLimitsUpdateResponse(string Message, RiskLimitsResponse Limits);

/// <summary>Trading status response</summary>
/// <param name="IsTradingEnabled">Whether trading is currently enabled</param>
/// <param name="Status">Status as string (Active/Paused)</param>
/// <param name="PausedAt">When trading was paused (if applicable)</param>
/// <param name="PausedBy">Who paused trading</param>
/// <param name="PauseReason">Reason for pause</param>
public record TradingStatusResponse(
    bool IsTradingEnabled,
    string Status,
    DateTime? PausedAt,
    string? PausedBy,
    string? PauseReason
);

/// <summary>Trading pause response</summary>
/// <param name="Message">Result message</param>
/// <param name="IsTradingEnabled">Whether trading is currently enabled</param>
/// <param name="Status">Status as string (Active/Paused)</param>
/// <param name="PausedAt">When trading was paused</param>
/// <param name="PausedBy">Who paused trading</param>
/// <param name="PauseReason">Reason for pause</param>
public record TradingPauseResponse(
    string Message,
    bool IsTradingEnabled,
    string Status,
    DateTime? PausedAt,
    string? PausedBy,
    string? PauseReason
);

/// <summary>Trading resume response</summary>
/// <param name="Message">Result message</param>
/// <param name="IsTradingEnabled">Whether trading is currently enabled</param>
/// <param name="Status">Status as string (Active/Paused)</param>
public record TradingResumeResponse(string Message, bool IsTradingEnabled, string Status);

/// <summary>Error response</summary>
/// <param name="Error">Error message</param>
public record ErrorResponse(string Error);

/// <summary>Trade not found error</summary>
/// <param name="Error">Error message</param>
/// <param name="TradeId">The requested trade ID</param>
public record TradeNotFoundResponse(string Error, Guid TradeId);

/// <summary>Validation errors response</summary>
/// <param name="Errors">List of validation errors</param>
public record ValidationErrorResponse(IEnumerable<string> Errors);
