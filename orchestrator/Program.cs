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

app.Run();
