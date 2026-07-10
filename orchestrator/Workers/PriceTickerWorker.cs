using QuantFlow.Orchestrator.Channels;
using QuantFlow.Protos;

namespace QuantFlow.Orchestrator.Workers;

public class PriceTickerOptions
{
    public string[] Assets { get; set; } = ["BTC", "ETH", "SPY"];
    public int TickIntervalMs { get; set; } = 1000;
    public Dictionary<string, decimal> InitialPrices { get; set; } = new()
    {
        ["BTC"] = 67500.00m,
        ["ETH"] = 3450.00m,
        ["SPY"] = 542.50m
    };
    public Dictionary<string, decimal> Volatility { get; set; } = new()
    {
        ["BTC"] = 0.02m,
        ["ETH"] = 0.025m,
        ["SPY"] = 0.005m
    };
}

public class PriceTickerWorker : IHostedService
{
    private readonly IPriceTickChannel _channel;
    private readonly ILogger<PriceTickerWorker> _logger;
    private readonly PriceTickerOptions _options;
    private readonly Dictionary<string, decimal> _currentPrices;
    private readonly Random _random;
    private CancellationTokenSource? _cts;
    private Task? _executingTask;

    public PriceTickerWorker(
        IPriceTickChannel channel,
        ILogger<PriceTickerWorker> logger,
        IConfiguration configuration)
    {
        _channel = channel;
        _logger = logger;
        _options = new PriceTickerOptions();
        configuration.GetSection("PriceTicker").Bind(_options);
        _currentPrices = new Dictionary<string, decimal>(_options.InitialPrices);
        _random = new Random();

        foreach (var asset in _options.Assets)
        {
            if (!_currentPrices.ContainsKey(asset))
            {
                _currentPrices[asset] = 100.00m;
            }
        }
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("PriceTickerWorker starting for assets: {Assets}",
            string.Join(", ", _options.Assets));

        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _executingTask = ExecuteAsync(_cts.Token);

        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("PriceTickerWorker stopping");

        if (_executingTask == null)
        {
            return;
        }

        _cts?.Cancel();

        await Task.WhenAny(_executingTask, Task.Delay(Timeout.Infinite, cancellationToken));
    }

    private async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(_options.TickIntervalMs, stoppingToken);

                foreach (var asset in _options.Assets)
                {
                    var tick = GenerateTick(asset);
                    await _channel.PublishAsync(tick, stoppingToken);

                    _logger.LogDebug("Published tick: {Asset} @ {Price:F2}",
                        tick.Asset, tick.Price);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generating price ticks");
            }
        }
    }

    private PriceTick GenerateTick(string asset)
    {
        var currentPrice = _currentPrices[asset];
        var volatility = _options.Volatility.GetValueOrDefault(asset, 0.01m);

        var change = GenerateRandomWalkChange(volatility);
        var newPrice = currentPrice * (1m + change);
        newPrice = Math.Max(newPrice, currentPrice * 0.5m);

        _currentPrices[asset] = newPrice;

        return new PriceTick
        {
            Asset = asset,
            Price = (double)newPrice,
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };
    }

    private decimal GenerateRandomWalkChange(decimal volatility)
    {
        var u1 = 1.0 - _random.NextDouble();
        var u2 = 1.0 - _random.NextDouble();
        var standardNormal = Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Sin(2.0 * Math.PI * u2);
        return (decimal)standardNormal * volatility;
    }
}
