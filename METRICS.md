# QuantFlow Metrics Documentation

This document describes the Prometheus metrics exposed by each QuantFlow service for monitoring and observability.

## Metrics Endpoints

| Service | Endpoint | Default Port |
|---------|----------|--------------|
| C# Orchestrator | `http://localhost:8080/metrics` | 8080 |
| Python Signal Engine | `http://localhost:8080/metrics` | 8080 |
| Rust Execution Layer | `http://localhost:8081/metrics` | 8081 |

## C# Orchestrator Metrics

### Counters

| Metric | Labels | Description |
|--------|--------|-------------|
| `quantflow_trades_executed_total` | `asset`, `side` | Total number of trades successfully executed |
| `quantflow_signals_received_total` | `asset`, `signal_type` | Total number of signals received from signal engine |
| `quantflow_risk_rejections_total` | `reason` | Total number of trades rejected by risk manager |

### Histograms

| Metric | Description | Buckets |
|--------|-------------|---------|
| `quantflow_order_latency_seconds` | Order execution latency in seconds | Exponential (0.001s - 4s) |
| `quantflow_signal_latency_seconds` | Signal retrieval latency in seconds | Exponential (0.001s - 1s) |

### Gauges

| Metric | Description |
|--------|-------------|
| `quantflow_active_positions` | Current number of active positions |
| `quantflow_portfolio_equity` | Current portfolio equity value |
| `quantflow_trading_enabled` | Whether trading is enabled (1) or disabled (0) |

### Label Values

**`side`**: `BUY`, `SELL`

**`signal_type`**: `BUY`, `SELL`, `HOLD`

**`reason`** (risk rejections):
- `max_drawdown` - Trade rejected due to maximum drawdown limit
- `position_size` - Trade rejected due to position size limit
- `exposure` - Trade rejected due to exposure limit
- `min_order_value` - Trade rejected due to minimum order value
- `insufficient_position` - Sell rejected due to insufficient holdings
- `invalid_quantity` - Invalid quantity specified
- `invalid_price` - Invalid price specified
- `other` - Other rejection reasons

## Python Signal Engine Metrics

### Counters

| Metric | Labels | Description |
|--------|--------|-------------|
| `signal_engine_signals_generated_total` | `asset`, `signal_type` | Total number of signals generated |
| `signal_engine_price_ticks_received_total` | `asset` | Total number of price ticks received |

### Histograms

| Metric | Description | Buckets |
|--------|-------------|---------|
| `signal_engine_processing_latency_seconds` | Signal processing latency | 0.0001s - 1s |

### Gauges

| Metric | Description |
|--------|-------------|
| `signal_engine_active_price_windows` | Number of active price windows being tracked |

### Label Values

**`signal_type`**: `BUY`, `SELL`, `HOLD`

**`asset`**: Dynamic (e.g., `BTC`, `ETH`, `AAPL`)

## Rust Execution Layer Metrics

### Counters

| Metric | Labels | Description |
|--------|--------|-------------|
| `execution_layer_orders_executed_total` | `asset`, `side` | Total number of orders executed |
| `execution_layer_orders_rejected_total` | `reason` | Total number of orders rejected |

### Histograms

| Metric | Labels | Description | Buckets |
|--------|--------|-------------|---------|
| `execution_layer_execution_latency_seconds` | `asset` | Order execution latency | 0.001s - 1s |

### Gauges

| Metric | Description |
|--------|-------------|
| `execution_layer_active_executions` | Number of currently active executions |

### Label Values

**`side`**: `BUY`, `SELL`

**`reason`** (rejections):
- `shutdown` - Rejected during service shutdown
- `invalid_asset` - Empty or invalid asset specified
- `invalid_quantity` - Zero or negative quantity

## Prometheus Configuration

Example Prometheus scrape configuration:

```yaml
scrape_configs:
  - job_name: 'quantflow-orchestrator'
    static_configs:
      - targets: ['orchestrator:8080']
    metrics_path: /metrics

  - job_name: 'quantflow-signal-engine'
    static_configs:
      - targets: ['signal_engine:8080']
    metrics_path: /metrics

  - job_name: 'quantflow-execution-layer'
    static_configs:
      - targets: ['execution_layer:8081']
    metrics_path: /metrics
```

## Example PromQL Queries

### Trade Rate
```promql
rate(quantflow_trades_executed_total[5m])
```

### Risk Rejection Rate
```promql
rate(quantflow_risk_rejections_total[5m])
```

### Order Latency P95
```promql
histogram_quantile(0.95, rate(quantflow_order_latency_seconds_bucket[5m]))
```

### Signal Generation Rate by Type
```promql
sum by (signal_type) (rate(signal_engine_signals_generated_total[5m]))
```

### Execution Layer Throughput
```promql
rate(execution_layer_orders_executed_total[5m])
```

### Active Executions
```promql
execution_layer_active_executions
```

## Grafana Dashboard Suggestions

1. **Trading Overview**
   - Trades per minute (by asset/side)
   - Risk rejection rate
   - Portfolio equity over time

2. **Latency Dashboard**
   - Order latency histogram
   - Signal latency histogram
   - P50/P95/P99 latencies

3. **Signal Engine Performance**
   - Signals generated per minute
   - Price ticks processed
   - Active price windows

4. **Execution Layer Performance**
   - Orders executed per minute
   - Execution latency distribution
   - Active executions gauge
