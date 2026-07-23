using Prometheus;

namespace QuantFlow.Orchestrator.Services;

public interface IMetricsService
{
    void IncrementTradesExecuted(string asset, string side);
    void IncrementSignalsReceived(string asset, string signalType);
    void IncrementRiskRejections(string reason);
    void RecordOrderLatency(double latencyMs);
    void RecordSignalLatency(double latencyMs);
}

public class MetricsService : IMetricsService
{
    private static readonly Counter TradesExecutedTotal = Metrics.CreateCounter(
        "quantflow_trades_executed_total",
        "Total number of trades executed",
        new CounterConfiguration
        {
            LabelNames = new[] { "asset", "side" }
        });

    private static readonly Counter SignalsReceivedTotal = Metrics.CreateCounter(
        "quantflow_signals_received_total",
        "Total number of signals received from signal engine",
        new CounterConfiguration
        {
            LabelNames = new[] { "asset", "signal_type" }
        });

    private static readonly Counter RiskRejectionsTotal = Metrics.CreateCounter(
        "quantflow_risk_rejections_total",
        "Total number of trades rejected by risk manager",
        new CounterConfiguration
        {
            LabelNames = new[] { "reason" }
        });

    private static readonly Histogram OrderLatencyHistogram = Metrics.CreateHistogram(
        "quantflow_order_latency_seconds",
        "Order execution latency in seconds",
        new HistogramConfiguration
        {
            Buckets = Histogram.ExponentialBuckets(0.001, 2, 12)
        });

    private static readonly Histogram SignalLatencyHistogram = Metrics.CreateHistogram(
        "quantflow_signal_latency_seconds",
        "Signal retrieval latency in seconds",
        new HistogramConfiguration
        {
            Buckets = Histogram.ExponentialBuckets(0.001, 2, 10)
        });

    private static readonly Gauge ActivePositionsGauge = Metrics.CreateGauge(
        "quantflow_active_positions",
        "Current number of active positions");

    private static readonly Gauge PortfolioEquityGauge = Metrics.CreateGauge(
        "quantflow_portfolio_equity",
        "Current portfolio equity value");

    private static readonly Gauge TradingEnabledGauge = Metrics.CreateGauge(
        "quantflow_trading_enabled",
        "Whether trading is currently enabled (1=enabled, 0=disabled)");

    public void IncrementTradesExecuted(string asset, string side)
    {
        TradesExecutedTotal.WithLabels(asset, side).Inc();
    }

    public void IncrementSignalsReceived(string asset, string signalType)
    {
        SignalsReceivedTotal.WithLabels(asset, signalType).Inc();
    }

    public void IncrementRiskRejections(string reason)
    {
        var normalizedReason = NormalizeRejectionReason(reason);
        RiskRejectionsTotal.WithLabels(normalizedReason).Inc();
    }

    public void RecordOrderLatency(double latencyMs)
    {
        OrderLatencyHistogram.Observe(latencyMs / 1000.0);
    }

    public void RecordSignalLatency(double latencyMs)
    {
        SignalLatencyHistogram.Observe(latencyMs / 1000.0);
    }

    public static void UpdateActivePositions(int count)
    {
        ActivePositionsGauge.Set(count);
    }

    public static void UpdatePortfolioEquity(double equity)
    {
        PortfolioEquityGauge.Set(equity);
    }

    public static void UpdateTradingEnabled(bool enabled)
    {
        TradingEnabledGauge.Set(enabled ? 1 : 0);
    }

    private static string NormalizeRejectionReason(string reason)
    {
        if (reason.Contains("drawdown", StringComparison.OrdinalIgnoreCase))
            return "max_drawdown";
        if (reason.Contains("position size", StringComparison.OrdinalIgnoreCase))
            return "position_size";
        if (reason.Contains("exposure", StringComparison.OrdinalIgnoreCase))
            return "exposure";
        if (reason.Contains("order value", StringComparison.OrdinalIgnoreCase))
            return "min_order_value";
        if (reason.Contains("insufficient", StringComparison.OrdinalIgnoreCase))
            return "insufficient_position";
        if (reason.Contains("quantity", StringComparison.OrdinalIgnoreCase))
            return "invalid_quantity";
        if (reason.Contains("price", StringComparison.OrdinalIgnoreCase))
            return "invalid_price";
        return "other";
    }
}
