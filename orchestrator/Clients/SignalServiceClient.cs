using Grpc.Core;
using Grpc.Net.Client;
using Grpc.Net.Client.Configuration;
using QuantFlow.Protos;

namespace QuantFlow.Orchestrator.Clients;

public interface ISignalServiceClient : IDisposable
{
    Task<TradeSignal> GetSignalAsync(IEnumerable<PriceTick> priceTicks, CancellationToken cancellationToken = default);
    Task<TradeSignal> GetSignalAsync(PriceTick priceTick, CancellationToken cancellationToken = default);
}

public class SignalServiceClient : ISignalServiceClient
{
    private readonly GrpcChannel _channel;
    private readonly SignalService.SignalServiceClient _client;
    private readonly ILogger<SignalServiceClient> _logger;
    private readonly SignalServiceClientOptions _options;
    private bool _disposed;

    public SignalServiceClient(ILogger<SignalServiceClient> logger, IConfiguration configuration)
    {
        _logger = logger;
        _options = new SignalServiceClientOptions();
        configuration.GetSection("SignalService").Bind(_options);

        var serviceConfig = new ServiceConfig
        {
            MethodConfigs =
            {
                new MethodConfig
                {
                    Names = { MethodName.Default },
                    RetryPolicy = new RetryPolicy
                    {
                        MaxAttempts = _options.MaxRetryAttempts,
                        InitialBackoff = TimeSpan.FromMilliseconds(_options.InitialBackoffMs),
                        MaxBackoff = TimeSpan.FromMilliseconds(_options.MaxBackoffMs),
                        BackoffMultiplier = _options.BackoffMultiplier,
                        RetryableStatusCodes = { StatusCode.Unavailable, StatusCode.DeadlineExceeded }
                    }
                }
            }
        };

        var channelOptions = new GrpcChannelOptions
        {
            ServiceConfig = serviceConfig,
            HttpHandler = new SocketsHttpHandler
            {
                PooledConnectionIdleTimeout = TimeSpan.FromSeconds(_options.IdleTimeoutSeconds),
                KeepAlivePingDelay = TimeSpan.FromSeconds(_options.KeepAlivePingDelaySeconds),
                KeepAlivePingTimeout = TimeSpan.FromSeconds(_options.KeepAlivePingTimeoutSeconds),
                EnableMultipleHttp2Connections = true
            }
        };

        _channel = GrpcChannel.ForAddress(_options.Address, channelOptions);
        _client = new SignalService.SignalServiceClient(_channel);
    }

    public async Task<TradeSignal> GetSignalAsync(IEnumerable<PriceTick> priceTicks, CancellationToken cancellationToken = default)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(_options.DeadlineMs);

        using var call = _client.GetSignal(deadline: deadline, cancellationToken: cancellationToken);

        foreach (var tick in priceTicks)
        {
            _logger.LogDebug("Sending price tick: {Asset} @ {Price}", tick.Asset, tick.Price);
            await call.RequestStream.WriteAsync(tick, cancellationToken);
        }

        await call.RequestStream.CompleteAsync();

        var signal = await call.ResponseAsync;
        _logger.LogInformation("Received signal: {Asset} -> {Signal} (confidence: {Confidence:P2})",
            signal.Asset, signal.Signal, signal.Confidence);

        return signal;
    }

    public async Task<TradeSignal> GetSignalAsync(PriceTick priceTick, CancellationToken cancellationToken = default)
    {
        return await GetSignalAsync([priceTick], cancellationToken);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                _channel.Dispose();
            }
            _disposed = true;
        }
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }
}

public class SignalServiceClientOptions
{
    public string Address { get; set; } = "http://localhost:50051";
    public int MaxRetryAttempts { get; set; } = 5;
    public int InitialBackoffMs { get; set; } = 100;
    public int MaxBackoffMs { get; set; } = 5000;
    public double BackoffMultiplier { get; set; } = 2.0;
    public int DeadlineMs { get; set; } = 30000;
    public int IdleTimeoutSeconds { get; set; } = 60;
    public int KeepAlivePingDelaySeconds { get; set; } = 30;
    public int KeepAlivePingTimeoutSeconds { get; set; } = 10;
}
