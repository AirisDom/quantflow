using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using QuantFlow.Orchestrator.Configuration;
using QuantFlow.Orchestrator.Data;
using QuantFlow.Orchestrator.Services;

namespace QuantFlow.Orchestrator.Commands;

public static class SeedDataCommand
{
    public static async Task<int> ExecuteAsync(string[] args, string connectionString)
    {
        var clearExisting = args.Any(a =>
            a.Equals("--clear-existing=true", StringComparison.OrdinalIgnoreCase) ||
            a.Equals("--clear", StringComparison.OrdinalIgnoreCase));

        Console.WriteLine("QuantFlow Database Seeder");
        Console.WriteLine("=========================");
        Console.WriteLine();

        var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();
        optionsBuilder.UseNpgsql(connectionString);

        await using var dbContext = new AppDbContext(optionsBuilder.Options);

        Console.WriteLine("Ensuring database exists and migrations are applied...");
        await dbContext.Database.MigrateAsync();

        var portfolioOptions = Options.Create(new PortfolioSettings { InitialCash = 100000m });
        var portfolioService = new PortfolioService(portfolioOptions);

        var seeder = new DatabaseSeeder(dbContext, portfolioService, seed: 42);

        Console.WriteLine();
        if (clearExisting)
        {
            Console.WriteLine("Clearing existing data...");
        }

        Console.WriteLine("Generating sample trade data...");
        await seeder.SeedAsync(clearExisting);

        var tradeCount = await dbContext.TradeRecords.CountAsync();
        var assets = await dbContext.TradeRecords
            .Select(t => t.Asset)
            .Distinct()
            .ToListAsync();

        var dateRange = await dbContext.TradeRecords
            .Select(t => new { Min = t.Timestamp, Max = t.Timestamp })
            .GroupBy(_ => 1)
            .Select(g => new {
                Min = g.Min(x => x.Min),
                Max = g.Max(x => x.Max)
            })
            .FirstOrDefaultAsync();

        Console.WriteLine();
        Console.WriteLine("Seeding completed successfully!");
        Console.WriteLine();
        Console.WriteLine("Summary:");
        Console.WriteLine($"  Total trades: {tradeCount}");
        Console.WriteLine($"  Assets: {string.Join(", ", assets)}");
        if (dateRange != null)
        {
            Console.WriteLine($"  Date range: {dateRange.Min:yyyy-MM-dd} to {dateRange.Max:yyyy-MM-dd}");
        }

        var summary = portfolioService.GetSummary();
        Console.WriteLine();
        Console.WriteLine("Portfolio State (from last 30 days of trades):");
        Console.WriteLine($"  Cash Balance: ${summary.CashBalance:N2}");
        Console.WriteLine($"  Total Equity: ${summary.TotalEquity:N2}");
        Console.WriteLine($"  Realized P&L: ${summary.RealizedPnL:N2}");
        Console.WriteLine($"  Positions: {summary.Positions.Count}");

        foreach (var position in summary.Positions.Values.Take(5))
        {
            Console.WriteLine($"    - {position.Asset}: {position.Quantity:N4} @ ${position.AverageCost:N2}");
        }

        if (summary.Positions.Count > 5)
        {
            Console.WriteLine($"    ... and {summary.Positions.Count - 5} more");
        }

        return 0;
    }
}
