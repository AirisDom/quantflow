using System.Threading.Channels;
using QuantFlow.Protos;

namespace QuantFlow.Orchestrator.Channels;

public interface IPriceTickChannel
{
    ChannelReader<PriceTick> Reader { get; }
    ValueTask PublishAsync(PriceTick tick, CancellationToken cancellationToken = default);
}

public class PriceTickChannel : IPriceTickChannel
{
    private readonly Channel<PriceTick> _channel;

    public PriceTickChannel()
    {
        _channel = Channel.CreateBounded<PriceTick>(new BoundedChannelOptions(1000)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleWriter = false,
            SingleReader = false
        });
    }

    public ChannelReader<PriceTick> Reader => _channel.Reader;

    public ValueTask PublishAsync(PriceTick tick, CancellationToken cancellationToken = default)
    {
        return _channel.Writer.WriteAsync(tick, cancellationToken);
    }
}
