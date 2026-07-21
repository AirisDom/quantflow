using Google.Protobuf;
using QuantFlow.Protos;

namespace QuantFlow.Orchestrator.IntegrationTests.GrpcCommunication;

public class ProtoSerializationTests
{
    [Fact]
    public void PriceTick_SerializesAndDeserializes_Correctly()
    {
        var original = new PriceTick
        {
            Asset = "BTC",
            Price = 67891.23456,
            Timestamp = 1720000000000
        };

        var bytes = original.ToByteArray();
        var deserialized = PriceTick.Parser.ParseFrom(bytes);

        Assert.Equal(original.Asset, deserialized.Asset);
        Assert.Equal(original.Price, deserialized.Price);
        Assert.Equal(original.Timestamp, deserialized.Timestamp);
    }

    [Fact]
    public void TradeSignal_SerializesAndDeserializes_AllSignalTypes()
    {
        foreach (SignalType signalType in Enum.GetValues<SignalType>())
        {
            var original = new TradeSignal
            {
                Asset = "ETH",
                Signal = signalType,
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                Confidence = 0.75
            };

            var bytes = original.ToByteArray();
            var deserialized = TradeSignal.Parser.ParseFrom(bytes);

            Assert.Equal(original.Signal, deserialized.Signal);
        }
    }

    [Fact]
    public void OrderRequest_SerializesAndDeserializes_BuyOrder()
    {
        var original = new OrderRequest
        {
            Asset = "SPY",
            Quantity = 100.5,
            Side = OrderSide.SideBuy
        };

        var bytes = original.ToByteArray();
        var deserialized = OrderRequest.Parser.ParseFrom(bytes);

        Assert.Equal(original.Asset, deserialized.Asset);
        Assert.Equal(original.Quantity, deserialized.Quantity);
        Assert.Equal(OrderSide.SideBuy, deserialized.Side);
    }

    [Fact]
    public void OrderRequest_SerializesAndDeserializes_SellOrder()
    {
        var original = new OrderRequest
        {
            Asset = "BTC",
            Quantity = 0.00123456,
            Side = OrderSide.SideSell
        };

        var bytes = original.ToByteArray();
        var deserialized = OrderRequest.Parser.ParseFrom(bytes);

        Assert.Equal(original.Asset, deserialized.Asset);
        Assert.Equal(original.Quantity, deserialized.Quantity);
        Assert.Equal(OrderSide.SideSell, deserialized.Side);
    }

    [Fact]
    public void ExecutionReceipt_SerializesAndDeserializes_AllStatuses()
    {
        foreach (ExecutionStatus status in Enum.GetValues<ExecutionStatus>())
        {
            var original = new ExecutionReceipt
            {
                OrderId = Guid.NewGuid().ToString(),
                FillPrice = 50000.0,
                Status = status,
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };

            var bytes = original.ToByteArray();
            var deserialized = ExecutionReceipt.Parser.ParseFrom(bytes);

            Assert.Equal(original.Status, deserialized.Status);
        }
    }

    [Fact]
    public void ExecutionReceipt_SerializesAndDeserializes_WithAllFields()
    {
        var original = new ExecutionReceipt
        {
            OrderId = "test-order-abc-123",
            FillPrice = 67891.5,
            Status = ExecutionStatus.Filled,
            Timestamp = 1720123456789
        };

        var bytes = original.ToByteArray();
        var deserialized = ExecutionReceipt.Parser.ParseFrom(bytes);

        Assert.Equal("test-order-abc-123", deserialized.OrderId);
        Assert.Equal(67891.5, deserialized.FillPrice);
        Assert.Equal(ExecutionStatus.Filled, deserialized.Status);
        Assert.Equal(1720123456789, deserialized.Timestamp);
    }

    [Fact]
    public void PriceTick_HandlesEdgeCases_ZeroPrice()
    {
        var original = new PriceTick
        {
            Asset = "TEST",
            Price = 0.0,
            Timestamp = 0
        };

        var bytes = original.ToByteArray();
        var deserialized = PriceTick.Parser.ParseFrom(bytes);

        Assert.Equal(0.0, deserialized.Price);
        Assert.Equal(0L, deserialized.Timestamp);
    }

    [Fact]
    public void PriceTick_HandlesEdgeCases_VerySmallPrice()
    {
        var original = new PriceTick
        {
            Asset = "SHIB",
            Price = 0.00000001,
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };

        var bytes = original.ToByteArray();
        var deserialized = PriceTick.Parser.ParseFrom(bytes);

        Assert.Equal(0.00000001, deserialized.Price, precision: 15);
    }

    [Fact]
    public void PriceTick_HandlesEdgeCases_VeryLargePrice()
    {
        var original = new PriceTick
        {
            Asset = "TEST",
            Price = 999999999.99999,
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };

        var bytes = original.ToByteArray();
        var deserialized = PriceTick.Parser.ParseFrom(bytes);

        Assert.Equal(999999999.99999, deserialized.Price, precision: 5);
    }

    [Fact]
    public void OrderRequest_HandlesEdgeCases_EmptyAsset()
    {
        var original = new OrderRequest
        {
            Asset = "",
            Quantity = 1.0,
            Side = OrderSide.SideBuy
        };

        var bytes = original.ToByteArray();
        var deserialized = OrderRequest.Parser.ParseFrom(bytes);

        Assert.Equal("", deserialized.Asset);
    }

    [Fact]
    public void OrderRequest_HandlesEdgeCases_UnicodeAsset()
    {
        var original = new OrderRequest
        {
            Asset = "BTC-日本円",
            Quantity = 1.0,
            Side = OrderSide.SideBuy
        };

        var bytes = original.ToByteArray();
        var deserialized = OrderRequest.Parser.ParseFrom(bytes);

        Assert.Equal("BTC-日本円", deserialized.Asset);
    }

    [Fact]
    public void TradeSignal_DefaultsToHold_WhenNotSet()
    {
        var original = new TradeSignal
        {
            Asset = "BTC",
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            Confidence = 0.5
        };

        var bytes = original.ToByteArray();
        var deserialized = TradeSignal.Parser.ParseFrom(bytes);

        Assert.Equal(SignalType.Hold, deserialized.Signal);
    }

    [Fact]
    public void OrderSide_DefaultsToBuy_WhenNotSet()
    {
        var original = new OrderRequest
        {
            Asset = "BTC",
            Quantity = 1.0
        };

        var bytes = original.ToByteArray();
        var deserialized = OrderRequest.Parser.ParseFrom(bytes);

        Assert.Equal(OrderSide.SideBuy, deserialized.Side);
    }

    [Fact]
    public void ExecutionStatus_DefaultsToPending_WhenNotSet()
    {
        var original = new ExecutionReceipt
        {
            OrderId = "test",
            FillPrice = 50000.0,
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };

        var bytes = original.ToByteArray();
        var deserialized = ExecutionReceipt.Parser.ParseFrom(bytes);

        Assert.Equal(ExecutionStatus.Pending, deserialized.Status);
    }

    [Fact]
    public void Messages_AreJsonSerializable()
    {
        var priceTick = new PriceTick { Asset = "BTC", Price = 50000.0, Timestamp = 1720000000000 };
        var tradeSignal = new TradeSignal { Asset = "BTC", Signal = SignalType.Buy, Timestamp = 1720000000000, Confidence = 0.9 };
        var orderRequest = new OrderRequest { Asset = "BTC", Quantity = 1.0, Side = OrderSide.SideBuy };
        var executionReceipt = new ExecutionReceipt { OrderId = "123", FillPrice = 50000.0, Status = ExecutionStatus.Filled, Timestamp = 1720000000000 };

        var jsonFormatter = new JsonFormatter(JsonFormatter.Settings.Default);

        var tickJson = jsonFormatter.Format(priceTick);
        var signalJson = jsonFormatter.Format(tradeSignal);
        var orderJson = jsonFormatter.Format(orderRequest);
        var receiptJson = jsonFormatter.Format(executionReceipt);

        Assert.Contains("BTC", tickJson);
        Assert.Contains("BUY", signalJson);
        Assert.Contains("1", orderJson);
        Assert.Contains("FILLED", receiptJson);
    }

    [Fact]
    public void Messages_CanBeCloned()
    {
        var original = new PriceTick
        {
            Asset = "BTC",
            Price = 50000.0,
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };

        var clone = original.Clone();

        Assert.Equal(original.Asset, clone.Asset);
        Assert.Equal(original.Price, clone.Price);
        Assert.Equal(original.Timestamp, clone.Timestamp);
        Assert.NotSame(original, clone);
    }
}
