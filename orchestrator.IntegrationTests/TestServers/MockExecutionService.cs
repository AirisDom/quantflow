using Grpc.Core;
using QuantFlow.Protos;

namespace QuantFlow.Orchestrator.IntegrationTests.TestServers;

public class MockExecutionService : ExecutionService.ExecutionServiceBase
{
    private readonly Func<OrderRequest, ExecutionReceipt>? _receiptGenerator;
    private readonly Exception? _exceptionToThrow;
    private readonly int _delayMs;

    public List<OrderRequest> ReceivedOrders { get; } = new();
    public int CallCount { get; private set; }

    public MockExecutionService(
        Func<OrderRequest, ExecutionReceipt>? receiptGenerator = null,
        Exception? exceptionToThrow = null,
        int delayMs = 0)
    {
        _receiptGenerator = receiptGenerator;
        _exceptionToThrow = exceptionToThrow;
        _delayMs = delayMs;
    }

    public override async Task<ExecutionReceipt> ExecuteOrder(OrderRequest request, ServerCallContext context)
    {
        CallCount++;

        lock (ReceivedOrders)
        {
            ReceivedOrders.Add(request);
        }

        if (_delayMs > 0)
        {
            await Task.Delay(_delayMs, context.CancellationToken);
        }

        if (_exceptionToThrow != null)
        {
            throw _exceptionToThrow;
        }

        if (_receiptGenerator != null)
        {
            return _receiptGenerator(request);
        }

        return new ExecutionReceipt
        {
            OrderId = Guid.NewGuid().ToString(),
            FillPrice = 50000.0,
            Status = ExecutionStatus.Filled,
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };
    }
}
