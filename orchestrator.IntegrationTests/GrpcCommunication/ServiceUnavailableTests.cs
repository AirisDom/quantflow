using Grpc.Core;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using QuantFlow.Orchestrator.Clients;
using QuantFlow.Orchestrator.Configuration;
using QuantFlow.Orchestrator.IntegrationTests.TestServers;
using QuantFlow.Protos;

namespace QuantFlow.Orchestrator.IntegrationTests.GrpcCommunication;

public class ServiceUnavailableTests : IDisposable
{
    private readonly GrpcTestServerFactory _serverFactory;

    public ServiceUnavailableTests()
    {
        _serverFactory = new GrpcTestServerFactory();
    }

    private static int GetUniquePort() => 30000 + Random.Shared.Next(1, 10000);

    [Fact]
    public async Task SignalServiceClient_WhenServerNotRunning_ThrowsUnavailable()
    {
        var settings = new SignalServiceSettings
        {
            Address = $"http://localhost:{GetUniquePort()}",
            MaxRetryAttempts = 2,
            InitialBackoffMs = 10,
            MaxBackoffMs = 50,
            BackoffMultiplier = 1.5,
            DeadlineMs = 1000,
            IdleTimeoutSeconds = 30,
            KeepAlivePingDelaySeconds = 15,
            KeepAlivePingTimeoutSeconds = 5
        };

        using var client = new SignalServiceClient(
            NullLogger<SignalServiceClient>.Instance,
            Options.Create(settings));

        var tick = new PriceTick
        {
            Asset = "BTC",
            Price = 50000.0,
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };

        var exception = await Assert.ThrowsAsync<RpcException>(() => client.GetSignalAsync(tick));

        Assert.True(
            exception.StatusCode == StatusCode.Unavailable ||
            exception.StatusCode == StatusCode.Internal,
            $"Expected Unavailable or Internal but got {exception.StatusCode}");
    }

    [Fact]
    public async Task ExecutionServiceClient_WhenServerNotRunning_ThrowsUnavailable()
    {
        var settings = new ExecutionServiceSettings
        {
            Address = $"http://localhost:{GetUniquePort()}",
            MaxRetryAttempts = 2,
            InitialBackoffMs = 10,
            MaxBackoffMs = 50,
            BackoffMultiplier = 1.5,
            DeadlineMs = 1000,
            IdleTimeoutSeconds = 30,
            KeepAlivePingDelaySeconds = 15,
            KeepAlivePingTimeoutSeconds = 5,
            CircuitBreakerFailureThreshold = 10,
            CircuitBreakerResetTimeMs = 30000
        };

        using var client = new ExecutionServiceClient(
            NullLogger<ExecutionServiceClient>.Instance,
            Options.Create(settings));

        var order = new OrderRequest
        {
            Asset = "BTC",
            Quantity = 0.5,
            Side = OrderSide.SideBuy
        };

        var exception = await Assert.ThrowsAsync<RpcException>(() => client.ExecuteOrderAsync(order));

        Assert.True(
            exception.StatusCode == StatusCode.Unavailable ||
            exception.StatusCode == StatusCode.Internal,
            $"Expected Unavailable or Internal but got {exception.StatusCode}");
    }

    [Fact]
    public async Task SignalServiceClient_WhenServerStops_ThrowsOnNextCall()
    {
        var mockService = new MockSignalService();
        var (app, address) = _serverFactory.CreateSignalServiceServer(mockService);
        await app.StartAsync();

        var settings = new SignalServiceSettings
        {
            Address = address,
            MaxRetryAttempts = 2,
            InitialBackoffMs = 10,
            MaxBackoffMs = 50,
            BackoffMultiplier = 1.5,
            DeadlineMs = 1000,
            IdleTimeoutSeconds = 30,
            KeepAlivePingDelaySeconds = 15,
            KeepAlivePingTimeoutSeconds = 5
        };

        using var client = new SignalServiceClient(
            NullLogger<SignalServiceClient>.Instance,
            Options.Create(settings));

        var tick = new PriceTick
        {
            Asset = "BTC",
            Price = 50000.0,
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };

        var result = await client.GetSignalAsync(tick);
        Assert.NotNull(result);

        await app.StopAsync();
        await Task.Delay(100);

        await Assert.ThrowsAsync<RpcException>(() => client.GetSignalAsync(tick));
    }

    [Fact]
    public async Task ExecutionServiceClient_WhenServerStops_ThrowsOnNextCall()
    {
        var mockService = new MockExecutionService();
        var (app, address) = _serverFactory.CreateExecutionServiceServer(mockService);
        await app.StartAsync();

        var settings = new ExecutionServiceSettings
        {
            Address = address,
            MaxRetryAttempts = 2,
            InitialBackoffMs = 10,
            MaxBackoffMs = 50,
            BackoffMultiplier = 1.5,
            DeadlineMs = 1000,
            IdleTimeoutSeconds = 30,
            KeepAlivePingDelaySeconds = 15,
            KeepAlivePingTimeoutSeconds = 5,
            CircuitBreakerFailureThreshold = 10,
            CircuitBreakerResetTimeMs = 30000
        };

        using var client = new ExecutionServiceClient(
            NullLogger<ExecutionServiceClient>.Instance,
            Options.Create(settings));

        var order = new OrderRequest
        {
            Asset = "BTC",
            Quantity = 0.5,
            Side = OrderSide.SideBuy
        };

        var result = await client.ExecuteOrderAsync(order);
        Assert.NotNull(result);

        await app.StopAsync();
        await Task.Delay(100);

        await Assert.ThrowsAsync<RpcException>(() => client.ExecuteOrderAsync(order));
    }

    [Fact]
    public async Task SignalServiceClient_WhenInvalidAddress_ThrowsOnCall()
    {
        var settings = new SignalServiceSettings
        {
            Address = "http://invalid.hostname.that.does.not.exist:50051",
            MaxRetryAttempts = 2,
            InitialBackoffMs = 10,
            MaxBackoffMs = 50,
            BackoffMultiplier = 1.5,
            DeadlineMs = 2000,
            IdleTimeoutSeconds = 30,
            KeepAlivePingDelaySeconds = 15,
            KeepAlivePingTimeoutSeconds = 5
        };

        using var client = new SignalServiceClient(
            NullLogger<SignalServiceClient>.Instance,
            Options.Create(settings));

        var tick = new PriceTick
        {
            Asset = "BTC",
            Price = 50000.0,
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };

        await Assert.ThrowsAsync<RpcException>(() => client.GetSignalAsync(tick));
    }

    [Fact]
    public async Task ExecutionServiceClient_CircuitBreaker_OpensAfterFailures()
    {
        var settings = new ExecutionServiceSettings
        {
            Address = $"http://localhost:{GetUniquePort()}",
            MaxRetryAttempts = 2,
            InitialBackoffMs = 10,
            MaxBackoffMs = 50,
            BackoffMultiplier = 1.5,
            DeadlineMs = 500,
            IdleTimeoutSeconds = 30,
            KeepAlivePingDelaySeconds = 15,
            KeepAlivePingTimeoutSeconds = 5,
            CircuitBreakerFailureThreshold = 2,
            CircuitBreakerResetTimeMs = 10000
        };

        using var client = new ExecutionServiceClient(
            NullLogger<ExecutionServiceClient>.Instance,
            Options.Create(settings));

        var order = new OrderRequest
        {
            Asset = "BTC",
            Quantity = 0.5,
            Side = OrderSide.SideBuy
        };

        for (int i = 0; i < 2; i++)
        {
            try
            {
                await client.ExecuteOrderAsync(order);
            }
            catch (RpcException)
            {
            }
        }

        Assert.True(client.IsCircuitOpen);

        await Assert.ThrowsAsync<CircuitBreakerOpenException>(() => client.ExecuteOrderAsync(order));
    }

    [Fact]
    public async Task SignalServiceClient_WithDeadlineExceeded_ThrowsDeadlineExceeded()
    {
        var mockService = new MockSignalService(ticks =>
        {
            Thread.Sleep(2000);
            return new TradeSignal { Asset = "BTC", Signal = SignalType.Hold, Timestamp = 0, Confidence = 0.5 };
        });

        var (app, address) = _serverFactory.CreateSignalServiceServer(mockService);
        await app.StartAsync();

        try
        {
            var settings = new SignalServiceSettings
            {
                Address = address,
                MaxRetryAttempts = 2,
                InitialBackoffMs = 10,
                MaxBackoffMs = 50,
                BackoffMultiplier = 1.5,
                DeadlineMs = 100,
                IdleTimeoutSeconds = 30,
                KeepAlivePingDelaySeconds = 15,
                KeepAlivePingTimeoutSeconds = 5
            };

            using var client = new SignalServiceClient(
                NullLogger<SignalServiceClient>.Instance,
                Options.Create(settings));

            var tick = new PriceTick
            {
                Asset = "BTC",
                Price = 50000.0,
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };

            var exception = await Assert.ThrowsAsync<RpcException>(() => client.GetSignalAsync(tick));
            Assert.Equal(StatusCode.DeadlineExceeded, exception.StatusCode);
        }
        finally
        {
            await app.StopAsync();
        }
    }

    public void Dispose()
    {
        _serverFactory.Dispose();
    }
}
