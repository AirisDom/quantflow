using System.Globalization;
using Microsoft.EntityFrameworkCore;
using QuantFlow.Orchestrator.Services;

namespace QuantFlow.Orchestrator.Data;

public class DatabaseSeeder
{
    private readonly AppDbContext _dbContext;
    private readonly IPortfolioService _portfolioService;
    private readonly Random _random;

    private static readonly string[] Assets = ["BTC/USD", "ETH/USD", "AAPL", "GOOGL", "MSFT", "AMZN", "TSLA", "SPY"];

    private static readonly Dictionary<string, (decimal basePrice, decimal volatility)> AssetPrices = new()
    {
        ["BTC/USD"] = (45000m, 0.03m),
        ["ETH/USD"] = (2800m, 0.04m),
        ["AAPL"] = (175m, 0.02m),
        ["GOOGL"] = (140m, 0.025m),
        ["MSFT"] = (380m, 0.02m),
        ["AMZN"] = (170m, 0.025m),
        ["TSLA"] = (250m, 0.05m),
        ["SPY"] = (450m, 0.01m)
    };

    public DatabaseSeeder(AppDbContext dbContext, IPortfolioService portfolioService, int? seed = null)
    {
        _dbContext = dbContext;
        _portfolioService = portfolioService;
        _random = seed.HasValue ? new Random(seed.Value) : new Random();
    }

    public async Task SeedAsync(bool clearExisting = false, CancellationToken cancellationToken = default)
    {
        if (clearExisting)
        {
            await ClearExistingDataAsync(cancellationToken);
        }

        var existingCount = await _dbContext.TradeRecords.CountAsync(cancellationToken);
        if (existingCount > 0 && !clearExisting)
        {
            return;
        }

        var trades = GenerateHistoricalTrades();
        await _dbContext.TradeRecords.AddRangeAsync(trades, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);

        SeedPortfolioState(trades);
    }

    private async Task ClearExistingDataAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _dbContext.TradeRecords.ExecuteDeleteAsync(cancellationToken);
        }
        catch (InvalidOperationException)
        {
            var allRecords = await _dbContext.TradeRecords.ToListAsync(cancellationToken);
            _dbContext.TradeRecords.RemoveRange(allRecords);
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
    }

    private List<TradeRecord> GenerateHistoricalTrades()
    {
        var trades = new List<TradeRecord>();
        var endDate = DateTime.UtcNow;
        var startDate = endDate.AddDays(-90);

        var assetPriceState = AssetPrices.ToDictionary(
            kvp => kvp.Key,
            kvp => kvp.Value.basePrice * (0.9m + (decimal)_random.NextDouble() * 0.2m)
        );

        var currentDate = startDate;

        while (currentDate < endDate)
        {
            var tradesPerDay = _random.Next(2, 8);

            for (var i = 0; i < tradesPerDay; i++)
            {
                var asset = Assets[_random.Next(Assets.Length)];
                var (basePrice, volatility) = AssetPrices[asset];

                var priceChange = (decimal)(_random.NextDouble() * 2 - 1) * basePrice * volatility;
                assetPriceState[asset] = Math.Max(basePrice * 0.5m, assetPriceState[asset] + priceChange);
                var price = assetPriceState[asset];

                var slippage = 1m + (decimal)(_random.NextDouble() * 0.002 - 0.001);
                price *= slippage;

                var side = _random.NextDouble() > 0.5 ? "BUY" : "SELL";

                decimal quantity;
                if (asset.Contains("USD"))
                {
                    quantity = Math.Round((decimal)(_random.NextDouble() * 0.5 + 0.01), 8);
                }
                else
                {
                    quantity = Math.Round((decimal)(_random.NextDouble() * 50 + 1), 2);
                }

                var tradeHour = _random.Next(9, 16);
                var tradeMinute = _random.Next(0, 60);
                var tradeSecond = _random.Next(0, 60);
                var tradeTime = currentDate.Date
                    .AddHours(tradeHour)
                    .AddMinutes(tradeMinute)
                    .AddSeconds(tradeSecond)
                    .AddMilliseconds(_random.Next(0, 1000));

                trades.Add(new TradeRecord
                {
                    Id = Guid.NewGuid(),
                    OrderId = $"ORD-{tradeTime:yyyyMMdd}-{Guid.NewGuid().ToString()[..8].ToUpper(CultureInfo.InvariantCulture)}",
                    Asset = asset,
                    Side = side,
                    Quantity = quantity,
                    Price = Math.Round(price, asset.Contains("USD", StringComparison.OrdinalIgnoreCase) ? 2 : 4),
                    Timestamp = tradeTime
                });
            }

            currentDate = currentDate.AddDays(1);
        }

        return trades.OrderBy(t => t.Timestamp).ToList();
    }

    private void SeedPortfolioState(List<TradeRecord> trades)
    {
        _portfolioService.Reset(100000m);

        var recentTrades = trades
            .Where(t => t.Timestamp > DateTime.UtcNow.AddDays(-30))
            .OrderBy(t => t.Timestamp)
            .ToList();

        foreach (var trade in recentTrades)
        {
            var executedTrade = new ExecutedTrade(
                trade.Asset,
                trade.Quantity,
                trade.Price,
                trade.Side == "BUY" ? TradeSide.Buy : TradeSide.Sell,
                trade.Timestamp
            );
            _portfolioService.UpdatePositionAfterTrade(executedTrade);
        }

        foreach (var asset in Assets)
        {
            if (AssetPrices.TryGetValue(asset, out var priceInfo))
            {
                var currentPrice = priceInfo.basePrice * (1m + (decimal)(_random.NextDouble() * 0.1 - 0.05));
                _portfolioService.UpdatePrice(asset, currentPrice);
            }
        }
    }
}
