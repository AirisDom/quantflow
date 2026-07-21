using Grpc.Core;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using QuantFlow.Orchestrator.Clients;
using QuantFlow.Orchestrator.Configuration;
using QuantFlow.Orchestrator.IntegrationTests.TestServers;
using QuantFlow.Protos;

namespace QuantFlow.Orchestrator.IntegrationTests.GrpcCommunication;

public class SignalServiceClientIntegrationTests : IDisposable
{
    private readonly GrpcTestServerFactory _serverFactory;

    public SignalServiceClientIntegrationTests()
    {
        _serverFactory = new GrpcTestServerFactory();
    }

    private SignalServiceSettings CreateSettings(string address) => new()
    {
        Address = address,
        MaxRetryAttempts = 2,
        InitialBackoffMs = 50,
        MaxBackoffMs = 200,
        BackoffMultiplier = 1.5,
        DeadlineMs = 5000,
        IdleTimeoutSeconds = 30,
        KeepAlivePingDelaySeconds = 15,
        KeepAlivePingTimeoutSeconds = 5
    };

    [Fact]
    public async Task GetSignalAsync_WithSingleTick_SendsTickAndReceivesSignal()
    {
        var expectedSignal = new TradeSignal
        {
            Asset = "BTC",
            Signal = SignalType.Buy,
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            Confidence = 0.85
        };

        var mockService = new MockSignalService(ticks => expectedSignal);
        var (app, address) = _serverFactory.CreateSignalServiceServer(mockService);
        await app.StartAsync();

        try
        {
            using var client = new SignalServiceClient(
                NullLogger<SignalServiceClient>.Instance,
                Options.Create(CreateSettings(address)));

            var tick = new PriceTick
            {
                Asset = "BTC",
                Price = 50000.0,
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };

            var result = await client.GetSignalAsync(tick);

            Assert.Equal("BTC", result.Asset);
            Assert.Equal(SignalType.Buy, result.Signal);
            Assert.Equal(0.85, result.Confidence);
            Assert.Single(mockService.ReceivedTicks);
            Assert.Equal(1, mockService.CallCount);
        }
        finally
        {
            await app.StopAsync();
        }
    }

    [Fact]
    public async Task GetSignalAsync_WithMultipleTicks_SendsAllTicksInStream()
    {
        var mockService = new MockSignalService(ticks => new TradeSignal
        {
            Asset = ticks.Last().Asset,
            Signal = SignalType.Sell,
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            Confidence = ticks.Count / 10.0
        });

        var (app, address) = _serverFactory.CreateSignalServiceServer(mockService);
        await app.StartAsync();

        try
        {
            using var client = new SignalServiceClient(
                NullLogger<SignalServiceClient>.Instance,
                Options.Create(CreateSettings(address)));

            var ticks = Enumerable.Range(0, 5).Select(i => new PriceTick
            {
                Asset = "ETH",
                Price = 3000.0 + i * 10,
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + i
            }).ToList();

            var result = await client.GetSignalAsync(ticks);

            Assert.Equal("ETH", result.Asset);
            Assert.Equal(SignalType.Sell, result.Signal);
            Assert.Equal(0.5, result.Confidence);
            Assert.Equal(5, mockService.ReceivedTicks.Count);
        }
        finally
        {
            await app.StopAsync();
        }
    }

    [Fact]
    public async Task GetSignalAsync_WhenServiceUnavailable_ThrowsRpcException()
    {
        using var client = new SignalServiceClient(
            NullLogger<SignalServiceClient>.Instance,
            Options.Create(CreateSettings("http://localhost:19999")));

        var tick = new PriceTick
        {
            Asset = "BTC",
            Price = 50000.0,
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };

        await Assert.ThrowsAsync<RpcException>(() => client.GetSignalAsync(tick));
    }

    [Fact]
    public async Task GetSignalAsync_WhenServiceThrowsError_PropagatesException()
    {
        var mockService = new MockSignalService(
            exceptionToThrow: new RpcException(new Status(StatusCode.Internal, "Simulated error")));

        var (app, address) = _serverFactory.CreateSignalServiceServer(mockService);
        await app.StartAsync();

        try
        {
            using var client = new SignalServiceClient(
                NullLogger<SignalServiceClient>.Instance,
                Options.Create(CreateSettings(address)));

            var tick = new PriceTick
            {
                Asset = "BTC",
                Price = 50000.0,
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };

            var exception = await Assert.ThrowsAsync<RpcException>(() => client.GetSignalAsync(tick));
            Assert.Equal(StatusCode.Internal, exception.StatusCode);
        }
        finally
        {
            await app.StopAsync();
        }
    }

    [Fact]
    public async Task GetSignalAsync_WithCancellation_ThrowsCancelledRpcException()
    {
        var mockService = new MockSignalService();
        var (app, address) = _serverFactory.CreateSignalServiceServer(mockService);
        await app.StartAsync();

        try
        {
            using var client = new SignalServiceClient(
                NullLogger<SignalServiceClient>.Instance,
                Options.Create(CreateSettings(address)));

            var tick = new PriceTick
            {
                Asset = "BTC",
                Price = 50000.0,
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };

            using var cts = new CancellationTokenSource();
            cts.Cancel();

            var exception = await Assert.ThrowsAsync<RpcException>(() =>
                client.GetSignalAsync(tick, cts.Token));
            Assert.Equal(StatusCode.Cancelled, exception.StatusCode);
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
