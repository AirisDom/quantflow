using Grpc.Core;
using QuantFlow.Protos;

namespace QuantFlow.Orchestrator.IntegrationTests.TestServers;

public class MockSignalService : SignalService.SignalServiceBase
{
    private readonly Func<List<PriceTick>, TradeSignal>? _signalGenerator;
    private readonly Exception? _exceptionToThrow;

    public List<PriceTick> ReceivedTicks { get; } = new();
    public int CallCount { get; private set; }

    public MockSignalService(Func<List<PriceTick>, TradeSignal>? signalGenerator = null, Exception? exceptionToThrow = null)
    {
        _signalGenerator = signalGenerator;
        _exceptionToThrow = exceptionToThrow;
    }

    public override async Task<TradeSignal> GetSignal(IAsyncStreamReader<PriceTick> requestStream, ServerCallContext context)
    {
        CallCount++;

        if (_exceptionToThrow != null)
        {
            throw _exceptionToThrow;
        }

        var ticks = new List<PriceTick>();
        await foreach (var tick in requestStream.ReadAllAsync(context.CancellationToken))
        {
            ticks.Add(tick);
            lock (ReceivedTicks)
            {
                ReceivedTicks.Add(tick);
            }
        }

        if (_signalGenerator != null)
        {
            return _signalGenerator(ticks);
        }

        var lastTick = ticks.LastOrDefault();
        return new TradeSignal
        {
            Asset = lastTick?.Asset ?? "UNKNOWN",
            Signal = SignalType.Hold,
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            Confidence = 0.5
        };
    }
}
