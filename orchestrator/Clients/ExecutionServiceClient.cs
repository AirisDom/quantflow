using Grpc.Core;
using Grpc.Net.Client;
using Grpc.Net.Client.Configuration;
using Microsoft.Extensions.Options;
using QuantFlow.Orchestrator.Configuration;
using QuantFlow.Protos;

namespace QuantFlow.Orchestrator.Clients;

public interface IExecutionServiceClient : IDisposable
{
    Task<ExecutionReceipt> ExecuteOrderAsync(OrderRequest order, CancellationToken cancellationToken = default);
    bool IsCircuitOpen { get; }
    Task<bool> CheckHealthAsync(CancellationToken cancellationToken = default);
}

public class ExecutionServiceClient : IExecutionServiceClient
{
    private readonly GrpcChannel _channel;
    private readonly ExecutionService.ExecutionServiceClient _client;
    private readonly ILogger<ExecutionServiceClient> _logger;
    private readonly ExecutionServiceSettings _options;
    private readonly object _circuitLock = new();
    private bool _disposed;

    private CircuitState _circuitState = CircuitState.Closed;
    private int _failureCount;
    private DateTime _circuitOpenedAt;

    public bool IsCircuitOpen => _circuitState == CircuitState.Open;

    public ExecutionServiceClient(ILogger<ExecutionServiceClient> logger, IOptions<ExecutionServiceSettings> options)
    {
        _logger = logger;
        _options = options.Value;

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
        _client = new ExecutionService.ExecutionServiceClient(_channel);
    }

    public async Task<ExecutionReceipt> ExecuteOrderAsync(OrderRequest order, CancellationToken cancellationToken = default)
    {
        EnsureCircuitAllowsRequest();

        var deadline = DateTime.UtcNow.AddMilliseconds(_options.DeadlineMs);

        try
        {
            _logger.LogInformation("Sending order: {Asset} {Side} {Quantity}",
                order.Asset, order.Side, order.Quantity);

            var receipt = await _client.ExecuteOrderAsync(order, deadline: deadline, cancellationToken: cancellationToken);

            _logger.LogInformation("Order executed: {OrderId} @ {FillPrice} - {Status}",
                receipt.OrderId, receipt.FillPrice, receipt.Status);

            OnSuccess();
            return receipt;
        }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.Unavailable ||
                                       ex.StatusCode == StatusCode.DeadlineExceeded ||
                                       ex.StatusCode == StatusCode.Internal)
        {
            OnFailure(ex);
            throw;
        }
    }

    private void EnsureCircuitAllowsRequest()
    {
        lock (_circuitLock)
        {
            switch (_circuitState)
            {
                case CircuitState.Open:
                    var timeSinceOpen = DateTime.UtcNow - _circuitOpenedAt;
                    if (timeSinceOpen >= TimeSpan.FromMilliseconds(_options.CircuitBreakerResetTimeMs))
                    {
                        _circuitState = CircuitState.HalfOpen;
                        _logger.LogInformation("Circuit breaker transitioning to half-open state");
                    }
                    else
                    {
                        _logger.LogWarning("Circuit breaker is open, rejecting request");
                        throw new CircuitBreakerOpenException(
                            $"Circuit breaker is open. Retry after {_options.CircuitBreakerResetTimeMs - timeSinceOpen.TotalMilliseconds:F0}ms");
                    }
                    break;

                case CircuitState.HalfOpen:
                    _logger.LogDebug("Circuit breaker is half-open, allowing test request");
                    break;

                case CircuitState.Closed:
                default:
                    break;
            }
        }
    }

    private void OnSuccess()
    {
        lock (_circuitLock)
        {
            if (_circuitState == CircuitState.HalfOpen)
            {
                _logger.LogInformation("Circuit breaker closing after successful request");
            }
            _circuitState = CircuitState.Closed;
            _failureCount = 0;
        }
    }

    private void OnFailure(Exception ex)
    {
        lock (_circuitLock)
        {
            _failureCount++;
            _logger.LogWarning(ex, "Execution service call failed. Failure count: {FailureCount}/{Threshold}",
                _failureCount, _options.CircuitBreakerFailureThreshold);

            if (_circuitState == CircuitState.HalfOpen ||
                _failureCount >= _options.CircuitBreakerFailureThreshold)
            {
                _circuitState = CircuitState.Open;
                _circuitOpenedAt = DateTime.UtcNow;
                _logger.LogWarning("Circuit breaker opened due to {FailureCount} consecutive failures",
                    _failureCount);
            }
        }
    }

    public async Task<bool> CheckHealthAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(5));
            var state = _channel.State;
            if (state == Grpc.Core.ConnectivityState.Ready)
            {
                return true;
            }
            await _channel.ConnectAsync(cts.Token);
            return _channel.State == Grpc.Core.ConnectivityState.Ready;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Execution service health check failed");
            return false;
        }
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

    private enum CircuitState
    {
        Closed,
        Open,
        HalfOpen
    }
}

public class CircuitBreakerOpenException : Exception
{
    public CircuitBreakerOpenException(string message) : base(message) { }
}
