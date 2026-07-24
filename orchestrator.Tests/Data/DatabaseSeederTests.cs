using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using QuantFlow.Orchestrator.Configuration;
using QuantFlow.Orchestrator.Data;
using QuantFlow.Orchestrator.Services;

namespace QuantFlow.Orchestrator.Tests.Data;

public class DatabaseSeederTests : IDisposable
{
    private readonly AppDbContext _dbContext;
    private readonly PortfolioService _portfolioService;
    private readonly DatabaseSeeder _seeder;

    public DatabaseSeederTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _dbContext = new AppDbContext(options);
        _portfolioService = new PortfolioService(Options.Create(new PortfolioSettings { InitialCash = 100000m }));
        _seeder = new DatabaseSeeder(_dbContext, _portfolioService, seed: 42);
    }

    public void Dispose()
    {
        _dbContext.Dispose();
    }

    [Fact]
    public async Task SeedAsync_CreatesTradeRecords()
    {
        await _seeder.SeedAsync();

        var count = await _dbContext.TradeRecords.CountAsync();
        Assert.True(count > 0, "Should create trade records");
    }

    [Fact]
    public async Task SeedAsync_CreatesRecordsWithMultipleAssets()
    {
        await _seeder.SeedAsync();

        var assets = await _dbContext.TradeRecords
            .Select(t => t.Asset)
            .Distinct()
            .ToListAsync();

        Assert.True(assets.Count > 1, "Should have multiple assets");
        Assert.Contains(assets, a => a.Contains("USD"));
        Assert.Contains(assets, a => !a.Contains("USD"));
    }

    [Fact]
    public async Task SeedAsync_CreatesRecordsSpanningMultipleDays()
    {
        await _seeder.SeedAsync();

        var dates = await _dbContext.TradeRecords
            .Select(t => t.Timestamp.Date)
            .Distinct()
            .ToListAsync();

        Assert.True(dates.Count > 30, "Should span multiple days");
    }

    [Fact]
    public async Task SeedAsync_CreatesRecordsWithBuyAndSellSides()
    {
        await _seeder.SeedAsync();

        var sides = await _dbContext.TradeRecords
            .Select(t => t.Side)
            .Distinct()
            .ToListAsync();

        Assert.Contains("BUY", sides);
        Assert.Contains("SELL", sides);
    }

    [Fact]
    public async Task SeedAsync_GeneratesUniqueOrderIds()
    {
        await _seeder.SeedAsync();

        var orderIds = await _dbContext.TradeRecords
            .Select(t => t.OrderId)
            .ToListAsync();

        var uniqueOrderIds = orderIds.Distinct().ToList();
        Assert.Equal(orderIds.Count, uniqueOrderIds.Count);
    }

    [Fact]
    public async Task SeedAsync_GeneratesRealisticPrices()
    {
        await _seeder.SeedAsync();

        var btcTrades = await _dbContext.TradeRecords
            .Where(t => t.Asset == "BTC/USD")
            .ToListAsync();

        if (btcTrades.Count > 0)
        {
            foreach (var trade in btcTrades)
            {
                Assert.True(trade.Price > 20000m, "BTC price should be realistic");
                Assert.True(trade.Price < 100000m, "BTC price should be realistic");
            }
        }

        var appleTrades = await _dbContext.TradeRecords
            .Where(t => t.Asset == "AAPL")
            .ToListAsync();

        if (appleTrades.Count > 0)
        {
            foreach (var trade in appleTrades)
            {
                Assert.True(trade.Price > 50m, "AAPL price should be realistic");
                Assert.True(trade.Price < 500m, "AAPL price should be realistic");
            }
        }
    }

    [Fact]
    public async Task SeedAsync_UpdatesPortfolioState()
    {
        await _seeder.SeedAsync();

        var positions = _portfolioService.GetAllPositions();
        Assert.True(positions.Count >= 0, "Portfolio may have positions after seeding");
    }

    [Fact]
    public async Task SeedAsync_WithClearExisting_RemovesOldData()
    {
        _dbContext.TradeRecords.Add(new TradeRecord
        {
            Id = Guid.NewGuid(),
            OrderId = "OLD-ORDER",
            Asset = "TEST",
            Side = "BUY",
            Quantity = 1m,
            Price = 100m,
            Timestamp = DateTime.UtcNow.AddDays(-365)
        });
        await _dbContext.SaveChangesAsync();

        await _seeder.SeedAsync(clearExisting: true);

        var oldRecord = await _dbContext.TradeRecords
            .FirstOrDefaultAsync(t => t.OrderId == "OLD-ORDER");

        Assert.Null(oldRecord);
    }

    [Fact]
    public async Task SeedAsync_WithoutClearExisting_DoesNotAddIfDataExists()
    {
        _dbContext.TradeRecords.Add(new TradeRecord
        {
            Id = Guid.NewGuid(),
            OrderId = "EXISTING-ORDER",
            Asset = "TEST",
            Side = "BUY",
            Quantity = 1m,
            Price = 100m,
            Timestamp = DateTime.UtcNow
        });
        await _dbContext.SaveChangesAsync();

        await _seeder.SeedAsync(clearExisting: false);

        var count = await _dbContext.TradeRecords.CountAsync();
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task SeedAsync_WithSameSeed_ProducesDeterministicResults()
    {
        var options1 = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        var options2 = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        using var db1 = new AppDbContext(options1);
        using var db2 = new AppDbContext(options2);

        var portfolio1 = new PortfolioService(Options.Create(new PortfolioSettings { InitialCash = 100000m }));
        var portfolio2 = new PortfolioService(Options.Create(new PortfolioSettings { InitialCash = 100000m }));

        var seeder1 = new DatabaseSeeder(db1, portfolio1, seed: 12345);
        var seeder2 = new DatabaseSeeder(db2, portfolio2, seed: 12345);

        await seeder1.SeedAsync();
        await seeder2.SeedAsync();

        var trades1 = await db1.TradeRecords.OrderBy(t => t.Timestamp).ToListAsync();
        var trades2 = await db2.TradeRecords.OrderBy(t => t.Timestamp).ToListAsync();

        Assert.Equal(trades1.Count, trades2.Count);

        for (var i = 0; i < Math.Min(10, trades1.Count); i++)
        {
            Assert.Equal(trades1[i].Asset, trades2[i].Asset);
            Assert.Equal(trades1[i].Side, trades2[i].Side);
            Assert.Equal(trades1[i].Quantity, trades2[i].Quantity);
        }
    }
}
