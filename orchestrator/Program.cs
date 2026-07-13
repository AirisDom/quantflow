using Microsoft.EntityFrameworkCore;
using QuantFlow.Orchestrator.Channels;
using QuantFlow.Orchestrator.Clients;
using QuantFlow.Orchestrator.Data;
using QuantFlow.Orchestrator.Services;
using QuantFlow.Orchestrator.Workers;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddSingleton<IRiskManager, RiskManager>();
builder.Services.AddSingleton<IPortfolioService, PortfolioService>();
builder.Services.AddSingleton<ITradingControlService, TradingControlService>();
builder.Services.AddSingleton<ISignalServiceClient, SignalServiceClient>();
builder.Services.AddSingleton<IExecutionServiceClient, ExecutionServiceClient>();

builder.Services.AddSingleton<IPriceTickChannel, PriceTickChannel>();
builder.Services.AddHostedService<PriceTickerWorker>();
builder.Services.AddHostedService<TradingOrchestrator>();

builder.Services.AddOpenApi();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    dbContext.Database.Migrate();

    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.MapGet("/health", () =>
{
    var healthResponse = new
    {
        Status = "Healthy",
        Service = "QuantFlow.Orchestrator",
        Timestamp = DateTime.UtcNow
    };
    return Results.Ok(healthResponse);
})
.WithName("HealthCheck");

app.MapGet("/portfolio", (IPortfolioService portfolioService) =>
{
    var summary = portfolioService.GetSummary();
    var response = new
    {
        summary.CashBalance,
        summary.TotalMarketValue,
        summary.TotalEquity,
        summary.RealizedPnL,
        summary.UnrealizedPnL,
        summary.PeakEquity,
        Positions = summary.Positions.Values.Select(p => new
        {
            p.Asset,
            p.Quantity,
            p.AverageCost,
            p.CurrentPrice,
            p.MarketValue,
            p.UnrealizedPnL,
            p.UnrealizedPnLPercent
        })
    };
    return Results.Ok(response);
})
.WithName("GetPortfolio");

app.MapGet("/trades", async (AppDbContext db, int page = 1, int pageSize = 20) =>
{
    if (page < 1) page = 1;
    if (pageSize < 1) pageSize = 1;
    if (pageSize > 100) pageSize = 100;

    var totalCount = await db.TradeRecords.CountAsync();
    var totalPages = (int)Math.Ceiling(totalCount / (double)pageSize);

    var trades = await db.TradeRecords
        .OrderByDescending(t => t.Timestamp)
        .Skip((page - 1) * pageSize)
        .Take(pageSize)
        .Select(t => new
        {
            t.Id,
            t.OrderId,
            t.Asset,
            t.Side,
            t.Quantity,
            t.Price,
            t.Timestamp
        })
        .ToListAsync();

    var response = new
    {
        Page = page,
        PageSize = pageSize,
        TotalCount = totalCount,
        TotalPages = totalPages,
        Trades = trades
    };
    return Results.Ok(response);
})
.WithName("GetTrades");

app.MapGet("/trades/{id:guid}", async (Guid id, AppDbContext db) =>
{
    var trade = await db.TradeRecords.FindAsync(id);
    if (trade is null)
    {
        return Results.NotFound(new { Error = "Trade not found", TradeId = id });
    }

    var response = new
    {
        trade.Id,
        trade.OrderId,
        trade.Asset,
        trade.Side,
        trade.Quantity,
        trade.Price,
        trade.Timestamp
    };
    return Results.Ok(response);
})
.WithName("GetTradeById");

app.MapGet("/risk/limits", (IRiskManager riskManager) =>
{
    var limits = riskManager.GetCurrentLimits();
    var response = new
    {
        limits.MaxDrawdownPercent,
        limits.MaxPositionSizePercent,
        limits.MaxExposurePercent,
        limits.MinOrderValue,
        MaxDrawdownPercentFormatted = $"{limits.MaxDrawdownPercent:P2}",
        MaxPositionSizePercentFormatted = $"{limits.MaxPositionSizePercent:P2}",
        MaxExposurePercentFormatted = $"{limits.MaxExposurePercent:P2}"
    };
    return Results.Ok(response);
})
.WithName("GetRiskLimits");

app.MapPut("/risk/limits", (RiskLimitsUpdateRequest request, IRiskManager riskManager) =>
{
    var errors = new List<string>();

    if (request.MaxDrawdownPercent.HasValue)
    {
        if (request.MaxDrawdownPercent.Value < 0 || request.MaxDrawdownPercent.Value > 1)
            errors.Add("MaxDrawdownPercent must be between 0 and 1");
    }
    if (request.MaxPositionSizePercent.HasValue)
    {
        if (request.MaxPositionSizePercent.Value < 0 || request.MaxPositionSizePercent.Value > 1)
            errors.Add("MaxPositionSizePercent must be between 0 and 1");
    }
    if (request.MaxExposurePercent.HasValue)
    {
        if (request.MaxExposurePercent.Value < 0 || request.MaxExposurePercent.Value > 1)
            errors.Add("MaxExposurePercent must be between 0 and 1");
    }
    if (request.MinOrderValue.HasValue)
    {
        if (request.MinOrderValue.Value < 0)
            errors.Add("MinOrderValue must be non-negative");
    }

    if (errors.Count > 0)
    {
        return Results.BadRequest(new { Errors = errors });
    }

    var updatedLimits = riskManager.UpdateLimits(request);
    var response = new
    {
        Message = "Risk limits updated successfully",
        Limits = new
        {
            updatedLimits.MaxDrawdownPercent,
            updatedLimits.MaxPositionSizePercent,
            updatedLimits.MaxExposurePercent,
            updatedLimits.MinOrderValue,
            MaxDrawdownPercentFormatted = $"{updatedLimits.MaxDrawdownPercent:P2}",
            MaxPositionSizePercentFormatted = $"{updatedLimits.MaxPositionSizePercent:P2}",
            MaxExposurePercentFormatted = $"{updatedLimits.MaxExposurePercent:P2}"
        }
    };
    return Results.Ok(response);
})
.WithName("UpdateRiskLimits");

app.MapGet("/trading/status", (ITradingControlService tradingControl) =>
{
    var status = tradingControl.GetStatus();
    var response = new
    {
        status.IsTradingEnabled,
        Status = status.IsTradingEnabled ? "Active" : "Paused",
        status.PausedAt,
        status.PausedBy,
        status.PauseReason
    };
    return Results.Ok(response);
})
.WithName("GetTradingStatus");

app.MapPost("/trading/pause", (TradingPauseRequest? request, ITradingControlService tradingControl) =>
{
    var status = tradingControl.Pause(request?.Reason, request?.PausedBy);
    var response = new
    {
        Message = status.IsTradingEnabled ? "Trading was already paused" : "Trading paused successfully",
        status.IsTradingEnabled,
        Status = status.IsTradingEnabled ? "Active" : "Paused",
        status.PausedAt,
        status.PausedBy,
        status.PauseReason
    };
    return Results.Ok(response);
})
.WithName("PauseTrading");

app.MapPost("/trading/resume", (TradingResumeRequest? request, ITradingControlService tradingControl) =>
{
    var status = tradingControl.Resume(request?.ResumedBy);
    var response = new
    {
        Message = status.IsTradingEnabled ? "Trading resumed successfully" : "Trading was already active",
        status.IsTradingEnabled,
        Status = status.IsTradingEnabled ? "Active" : "Paused"
    };
    return Results.Ok(response);
})
.WithName("ResumeTrading");

app.Run();

public record TradingPauseRequest(string? Reason, string? PausedBy);
public record TradingResumeRequest(string? ResumedBy);
