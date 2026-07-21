using Grpc.Core;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using QuantFlow.Orchestrator.Clients;
using QuantFlow.Orchestrator.Configuration;
using QuantFlow.Orchestrator.IntegrationTests.TestServers;
using QuantFlow.Protos;

namespace QuantFlow.Orchestrator.IntegrationTests.GrpcCommunication;

public class ExecutionServiceClientIntegrationTests : IDisposable
{
    private readonly GrpcTestServerFactory _serverFactory;

    public ExecutionServiceClientIntegrationTests()
    {
        _serverFactory = new GrpcTestServerFactory();
    }

    private ExecutionServiceSettings CreateSettings(string address) => new()
    {
        Address = address,
        MaxRetryAttempts = 2,
        InitialBackoffMs = 50,
        MaxBackoffMs = 200,
        BackoffMultiplier = 1.5,
        DeadlineMs = 5000,
        IdleTimeoutSeconds = 30,
        KeepAlivePingDelaySeconds = 15,
        KeepAlivePingTimeoutSeconds = 5,
        CircuitBreakerFailureThreshold = 3,
        CircuitBreakerResetTimeMs = 1000
    };

    [Fact]
    public async Task ExecuteOrderAsync_WithValidOrder_ReturnsReceipt()
    {
        var expectedReceipt = new ExecutionReceipt
        {
            OrderId = "test-order-123",
            FillPrice = 50500.0,
            Status = ExecutionStatus.Filled,
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };

        var mockService = new MockExecutionService(order => expectedReceipt);
        var (app, address) = _serverFactory.CreateExecutionServiceServer(mockService);
        await app.StartAsync();

        try
        {
            using var client = new ExecutionServiceClient(
                NullLogger<ExecutionServiceClient>.Instance,
                Options.Create(CreateSettings(address)));

            var order = new OrderRequest
            {
                Asset = "BTC",
                Quantity = 0.5,
                Side = OrderSide.SideBuy
            };

            var result = await client.ExecuteOrderAsync(order);

            Assert.Equal("test-order-123", result.OrderId);
            Assert.Equal(50500.0, result.FillPrice);
            Assert.Equal(ExecutionStatus.Filled, result.Status);
            Assert.Single(mockService.ReceivedOrders);
            Assert.Equal(1, mockService.CallCount);
        }
        finally
        {
            await app.StopAsync();
        }
    }

    [Fact]
    public async Task ExecuteOrderAsync_WithSellOrder_CorrectlySerializesOrderSide()
    {
        OrderRequest? capturedOrder = null;
        var mockService = new MockExecutionService(order =>
        {
            capturedOrder = order;
            return new ExecutionReceipt
            {
                OrderId = "sell-order-456",
                FillPrice = 49500.0,
                Status = ExecutionStatus.Filled,
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };
        });

        var (app, address) = _serverFactory.CreateExecutionServiceServer(mockService);
        await app.StartAsync();

        try
        {
            using var client = new ExecutionServiceClient(
                NullLogger<ExecutionServiceClient>.Instance,
                Options.Create(CreateSettings(address)));

            var order = new OrderRequest
            {
                Asset = "ETH",
                Quantity = 2.5,
                Side = OrderSide.SideSell
            };

            await client.ExecuteOrderAsync(order);

            Assert.NotNull(capturedOrder);
            Assert.Equal("ETH", capturedOrder.Asset);
            Assert.Equal(2.5, capturedOrder.Quantity);
            Assert.Equal(OrderSide.SideSell, capturedOrder.Side);
        }
        finally
        {
            await app.StopAsync();
        }
    }

    [Fact]
    public async Task ExecuteOrderAsync_WhenServiceUnavailable_ThrowsRpcException()
    {
        using var client = new ExecutionServiceClient(
            NullLogger<ExecutionServiceClient>.Instance,
            Options.Create(CreateSettings("http://localhost:19998")));

        var order = new OrderRequest
        {
            Asset = "BTC",
            Quantity = 0.5,
            Side = OrderSide.SideBuy
        };

        await Assert.ThrowsAsync<RpcException>(() => client.ExecuteOrderAsync(order));
    }

    [Fact]
    public async Task ExecuteOrderAsync_WhenOrderRejected_ReturnsRejectedStatus()
    {
        var mockService = new MockExecutionService(order => new ExecutionReceipt
        {
            OrderId = "rejected-order",
            FillPrice = 0.0,
            Status = ExecutionStatus.Rejected,
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        });

        var (app, address) = _serverFactory.CreateExecutionServiceServer(mockService);
        await app.StartAsync();

        try
        {
            using var client = new ExecutionServiceClient(
                NullLogger<ExecutionServiceClient>.Instance,
                Options.Create(CreateSettings(address)));

            var order = new OrderRequest
            {
                Asset = "BTC",
                Quantity = 0.001,
                Side = OrderSide.SideBuy
            };

            var result = await client.ExecuteOrderAsync(order);

            Assert.Equal(ExecutionStatus.Rejected, result.Status);
        }
        finally
        {
            await app.StopAsync();
        }
    }

    [Fact]
    public async Task ExecuteOrderAsync_MultipleOrders_HandlesCorrectly()
    {
        var orderCount = 0;
        var mockService = new MockExecutionService(order => new ExecutionReceipt
        {
            OrderId = $"order-{Interlocked.Increment(ref orderCount)}",
            FillPrice = 50000.0 + orderCount,
            Status = ExecutionStatus.Filled,
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        });

        var (app, address) = _serverFactory.CreateExecutionServiceServer(mockService);
        await app.StartAsync();

        try
        {
            using var client = new ExecutionServiceClient(
                NullLogger<ExecutionServiceClient>.Instance,
                Options.Create(CreateSettings(address)));

            var tasks = Enumerable.Range(0, 5).Select(i => client.ExecuteOrderAsync(new OrderRequest
            {
                Asset = "BTC",
                Quantity = 0.1 * (i + 1),
                Side = i % 2 == 0 ? OrderSide.SideBuy : OrderSide.SideSell
            })).ToList();

            var results = await Task.WhenAll(tasks);

            Assert.Equal(5, results.Length);
            Assert.All(results, r => Assert.Equal(ExecutionStatus.Filled, r.Status));
            Assert.Equal(5, mockService.ReceivedOrders.Count);
        }
        finally
        {
            await app.StopAsync();
        }
    }

    [Fact]
    public async Task ExecuteOrderAsync_WhenServiceThrowsError_PropagatesException()
    {
        var mockService = new MockExecutionService(
            exceptionToThrow: new RpcException(new Status(StatusCode.Internal, "Exchange unavailable")));

        var (app, address) = _serverFactory.CreateExecutionServiceServer(mockService);
        await app.StartAsync();

        try
        {
            using var client = new ExecutionServiceClient(
                NullLogger<ExecutionServiceClient>.Instance,
                Options.Create(CreateSettings(address)));

            var order = new OrderRequest
            {
                Asset = "BTC",
                Quantity = 0.5,
                Side = OrderSide.SideBuy
            };

            var exception = await Assert.ThrowsAsync<RpcException>(() => client.ExecuteOrderAsync(order));
            Assert.Equal(StatusCode.Internal, exception.StatusCode);
        }
        finally
        {
            await app.StopAsync();
        }
    }

    [Fact]
    public async Task ExecuteOrderAsync_WithCancellation_ThrowsCancelledRpcException()
    {
        var mockService = new MockExecutionService(delayMs: 5000);
        var (app, address) = _serverFactory.CreateExecutionServiceServer(mockService);
        await app.StartAsync();

        try
        {
            using var client = new ExecutionServiceClient(
                NullLogger<ExecutionServiceClient>.Instance,
                Options.Create(CreateSettings(address)));

            var order = new OrderRequest
            {
                Asset = "BTC",
                Quantity = 0.5,
                Side = OrderSide.SideBuy
            };

            using var cts = new CancellationTokenSource();
            cts.Cancel();

            var exception = await Assert.ThrowsAsync<RpcException>(() =>
                client.ExecuteOrderAsync(order, cts.Token));
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
