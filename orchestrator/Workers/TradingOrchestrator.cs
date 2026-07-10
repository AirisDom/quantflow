using Microsoft.EntityFrameworkCore;
using QuantFlow.Orchestrator.Channels;
using QuantFlow.Orchestrator.Clients;
using QuantFlow.Orchestrator.Data;
using QuantFlow.Orchestrator.Services;
using QuantFlow.Protos;

namespace QuantFlow.Orchestrator.Workers;

public class TradingOrchestratorOptions
{
    public decimal DefaultOrderQuantity { get; set; } = 0.1m;
    public double MinConfidenceThreshold { get; set; } = 0.5;
    public bool EnableTrading { get; set; } = true;
}

public class TradingOrchestrator : IHostedService
{
    private readonly IPriceTickChannel _priceChannel;
    private readonly ISignalServiceClient _signalClient;
    private readonly IExecutionServiceClient _executionClient;
    private readonly IRiskManager _riskManager;
    private readonly IPortfolioService _portfolioService;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<TradingOrchestrator> _logger;
    private readonly TradingOrchestratorOptions _options;
    private CancellationTokenSource? _cts;
    private Task? _executingTask;

    public TradingOrchestrator(
        IPriceTickChannel priceChannel,
        ISignalServiceClient signalClient,
        IExecutionServiceClient executionClient,
        IRiskManager riskManager,
        IPortfolioService portfolioService,
        IServiceScopeFactory scopeFactory,
        ILogger<TradingOrchestrator> logger,
        IConfiguration configuration)
    {
        _priceChannel = priceChannel;
        _signalClient = signalClient;
        _executionClient = executionClient;
        _riskManager = riskManager;
        _portfolioService = portfolioService;
        _scopeFactory = scopeFactory;
        _logger = logger;
        _options = new TradingOrchestratorOptions();
        configuration.GetSection("TradingOrchestrator").Bind(_options);
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("TradingOrchestrator starting (trading enabled: {Enabled})", _options.EnableTrading);

        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _executingTask = ExecuteAsync(_cts.Token);

        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("TradingOrchestrator stopping");

        if (_executingTask == null)
            return;

        _cts?.Cancel();

        await Task.WhenAny(_executingTask, Task.Delay(Timeout.Infinite, cancellationToken));
    }

    private async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var priceTick in _priceChannel.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                await ProcessPriceTickAsync(priceTick, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing price tick for {Asset}", priceTick.Asset);
            }
        }
    }

    private async Task ProcessPriceTickAsync(PriceTick priceTick, CancellationToken cancellationToken)
    {
        _portfolioService.UpdatePrice(priceTick.Asset, (decimal)priceTick.Price);

        TradeSignal signal;
        try
        {
            signal = await _signalClient.GetSignalAsync(priceTick, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get signal for {Asset}, skipping", priceTick.Asset);
            return;
        }

        _logger.LogDebug("Signal received: {Asset} -> {Signal} (confidence: {Confidence:P2})",
            signal.Asset, signal.Signal, signal.Confidence);

        if (signal.Signal == SignalType.Hold)
        {
            _logger.LogDebug("HOLD signal for {Asset}, no action", signal.Asset);
            return;
        }

        if (signal.Confidence < _options.MinConfidenceThreshold)
        {
            _logger.LogInformation("Signal {Signal} for {Asset} rejected: confidence {Confidence:P2} below threshold {Threshold:P2}",
                signal.Signal, signal.Asset, signal.Confidence, _options.MinConfidenceThreshold);
            return;
        }

        var tradeSide = signal.Signal == SignalType.Buy ? TradeSide.Buy : TradeSide.Sell;
        var proposal = new TradeProposal(
            signal.Asset,
            _options.DefaultOrderQuantity,
            (decimal)priceTick.Price,
            tradeSide
        );

        var portfolioState = _portfolioService.GetPortfolioStateForRisk();
        var riskDecision = _riskManager.EvaluateTrade(proposal, portfolioState);

        if (!riskDecision.IsApproved)
        {
            _logger.LogWarning("Trade REJECTED by RiskManager: {Asset} {Side} {Quantity} @ {Price:F2} - {Reason}",
                proposal.Asset, proposal.Side, proposal.Quantity, proposal.Price, riskDecision.Reason);
            return;
        }

        _logger.LogInformation("Trade APPROVED by RiskManager: {Asset} {Side} {Quantity} @ {Price:F2}",
            proposal.Asset, proposal.Side, proposal.Quantity, proposal.Price);

        if (!_options.EnableTrading)
        {
            _logger.LogInformation("Trading disabled, skipping execution for {Asset}", proposal.Asset);
            return;
        }

        await ExecuteTradeAsync(proposal, cancellationToken);
    }

    private async Task ExecuteTradeAsync(TradeProposal proposal, CancellationToken cancellationToken)
    {
        if (_executionClient.IsCircuitOpen)
        {
            _logger.LogWarning("Execution circuit breaker is open, skipping trade for {Asset}", proposal.Asset);
            return;
        }

        var orderRequest = new OrderRequest
        {
            Asset = proposal.Asset,
            Quantity = (double)proposal.Quantity,
            Side = proposal.Side == TradeSide.Buy ? OrderSide.SideBuy : OrderSide.SideSell
        };

        ExecutionReceipt receipt;
        try
        {
            receipt = await _executionClient.ExecuteOrderAsync(orderRequest, cancellationToken);
        }
        catch (CircuitBreakerOpenException ex)
        {
            _logger.LogWarning(ex, "Circuit breaker prevented execution for {Asset}", proposal.Asset);
            return;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to execute order for {Asset}", proposal.Asset);
            return;
        }

        if (receipt.Status == ExecutionStatus.Filled)
        {
            _logger.LogInformation("Order FILLED: {OrderId} {Asset} {Side} @ {FillPrice:F2}",
                receipt.OrderId, proposal.Asset, proposal.Side, receipt.FillPrice);

            var executedTrade = new ExecutedTrade(
                proposal.Asset,
                proposal.Quantity,
                (decimal)receipt.FillPrice,
                proposal.Side,
                DateTime.UtcNow
            );
            _portfolioService.UpdatePositionAfterTrade(executedTrade);

            await RecordTradeAsync(proposal, receipt, cancellationToken);
        }
        else
        {
            _logger.LogWarning("Order {Status}: {OrderId} {Asset} {Side}",
                receipt.Status, receipt.OrderId, proposal.Asset, proposal.Side);
        }
    }

    private async Task RecordTradeAsync(TradeProposal proposal, ExecutionReceipt receipt, CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var tradeRecord = new TradeRecord
            {
                Id = Guid.NewGuid(),
                Asset = proposal.Asset,
                Side = proposal.Side.ToString(),
                Quantity = proposal.Quantity,
                Price = (decimal)receipt.FillPrice,
                Timestamp = DateTime.UtcNow
            };

            dbContext.TradeRecords.Add(tradeRecord);
            await dbContext.SaveChangesAsync(cancellationToken);

            _logger.LogDebug("Trade recorded to database: {TradeId}", tradeRecord.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to record trade for {Asset}", proposal.Asset);
        }
    }
}
