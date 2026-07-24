# QuantFlow

A cloud-native algorithmic trading orchestrator that routes workloads to the programming language best suited for the job. It uses **Python** to calculate real-time market signals, **Rust** to execute high-speed trades, and **C# (.NET 10)** as the central nervous system that orchestrates state, risk management, and the transaction ledger.

## Architecture

```
┌─────────────────────────────────────────────────────────────────────────────────┐
│                              QuantFlow Architecture                              │
└─────────────────────────────────────────────────────────────────────────────────┘

                                ┌───────────────────┐
                                │   REST Clients    │
                                │  (Portfolio API)  │
                                └─────────┬─────────┘
                                          │ HTTP/JSON
                                          ▼
┌─────────────────────────────────────────────────────────────────────────────────┐
│                          C# Orchestrator (.NET 10)                              │
│  ┌─────────────┐ ┌─────────────┐ ┌─────────────┐ ┌─────────────┐               │
│  │   REST API  │ │  Portfolio  │ │    Risk     │ │   Trading   │               │
│  │  Endpoints  │ │   Service   │ │   Manager   │ │   Control   │               │
│  └─────────────┘ └─────────────┘ └─────────────┘ └─────────────┘               │
│  ┌─────────────────────────────┐ ┌─────────────────────────────┐               │
│  │     Price Ticker Worker     │ │   Trading Orchestrator      │               │
│  │   (Background Service)      │ │   (Background Service)      │               │
│  └─────────────────────────────┘ └─────────────────────────────┘               │
│                    │                           │                                │
│                    │ gRPC                      │ gRPC                           │
│                    ▼                           ▼                                │
└─────────────────────────────────────────────────────────────────────────────────┘
         │                                                │
         │ gRPC (Streaming)                               │ gRPC (Unary)
         ▼                                                ▼
┌─────────────────────────┐                  ┌─────────────────────────┐
│  Python Signal Engine   │                  │  Rust Execution Layer   │
│  ┌───────────────────┐  │                  │  ┌───────────────────┐  │
│  │  NumPy Rolling    │  │                  │  │   Tokio Async     │  │
│  │  Price Window     │  │                  │  │   Runtime         │  │
│  └───────────────────┘  │                  │  └───────────────────┘  │
│  ┌───────────────────┐  │                  │  ┌───────────────────┐  │
│  │  Moving Average   │  │                  │  │  Mock Exchange    │  │
│  │  Crossover Logic  │  │                  │  │  Execution        │  │
│  └───────────────────┘  │                  │  └───────────────────┘  │
│         Port 50051      │                  │         Port 50052      │
└─────────────────────────┘                  └─────────────────────────┘

                                          │
                                          ▼
                              ┌───────────────────┐
                              │    PostgreSQL     │
                              │   Trade Records   │
                              │    Port 5432      │
                              └───────────────────┘
```

### Service Communication Flow

```
┌────────┐  PriceTick   ┌────────┐  TradeSignal  ┌────────┐  OrderRequest  ┌────────┐
│ Ticker │─────────────▶│ Signal │──────────────▶│  Risk  │───────────────▶│  Exec  │
│ Worker │   (Stream)   │ Engine │    (BUY/     │ Check  │    (if OK)     │ Layer  │
└────────┘              └────────┘    SELL/     └────────┘                └────────┘
                                      HOLD)           │                        │
                                                      ▼                        ▼
                                               ┌───────────┐           ┌───────────┐
                                               │ Portfolio │◀──────────│ Execution │
                                               │  Update   │  Receipt  │  Receipt  │
                                               └───────────┘           └───────────┘
                                                      │
                                                      ▼
                                               ┌───────────┐
                                               │ Database  │
                                               │  Record   │
                                               └───────────┘
```

## Technology Rationale

| Service | Language | Why This Choice |
|---------|----------|-----------------|
| **Orchestrator** | C# (.NET 10) | Enterprise-grade productivity for state management, Entity Framework Core for persistence, robust async patterns, excellent gRPC tooling |
| **Signal Engine** | Python 3 | Unmatched data science ecosystem with NumPy, rapid prototyping for quantitative algorithms, ideal for signal processing |
| **Execution Layer** | Rust | Zero-cost abstractions, memory safety without GC, predictable sub-millisecond latencies, perfect for high-frequency execution |

### Communication Protocol

All inter-service communication uses **gRPC** with Protocol Buffers for:
- Type-safe contracts across all three languages
- Efficient binary serialization
- Streaming support for real-time price data
- Built-in load balancing and health checking

## Prerequisites

| Requirement | Version | Download |
|-------------|---------|----------|
| .NET SDK | 10.0+ | [dotnet.microsoft.com](https://dotnet.microsoft.com/download) |
| Python | 3.10+ | [python.org](https://python.org/downloads) |
| Rust | Latest stable | [rustup.rs](https://rustup.rs) |
| Docker | 20.10+ | [docker.com](https://docker.com) (optional) |
| PostgreSQL | 14+ | [postgresql.org](https://postgresql.org) (or use Docker) |

## Quickstart

### Using Docker Compose (Recommended)

```bash
# Clone and navigate to the project
cd quantflow

# Copy environment template and set your password
cp .env.example .env
# Edit .env and set POSTGRES_PASSWORD

# Start all services
docker-compose up --build

# Access the API
curl http://localhost:8080/health
curl http://localhost:8080/portfolio
```

### Manual Development Setup

```bash
# Run the automated setup script
./scripts/setup-dev.sh    # Linux/macOS
.\scripts\setup-dev.ps1   # Windows PowerShell

# The script will:
# 1. Verify all prerequisites
# 2. Create .env from template
# 3. Set up Python virtual environment and dependencies
# 4. Build all three services
# 5. Generate protobuf stubs
```

## Development Setup

### Step-by-Step Manual Setup

#### 1. Environment Configuration

```bash
cp .env.example .env
# Edit .env with your database credentials
```

#### 2. Start PostgreSQL

```bash
# Using Docker
docker-compose up -d postgres

# Or connect to existing PostgreSQL and update .env
```

#### 3. Python Signal Engine

```bash
cd signal_engine
python3 -m venv .venv
source .venv/bin/activate  # Windows: .venv\Scripts\activate
pip install -r requirements.txt

# Generate protobuf stubs
python -m grpc_tools.protoc -I../shared \
    --python_out=. --grpc_python_out=. \
    ../shared/quantflow.proto

# Run the service
python main.py
```

#### 4. Rust Execution Layer

```bash
cd execution_layer
cargo build --release
cargo run --release
```

#### 5. C# Orchestrator

```bash
cd orchestrator
dotnet restore
dotnet ef database update  # Apply migrations
dotnet run
```

### Running All Services

```bash
# Terminal 1: PostgreSQL
docker-compose up postgres

# Terminal 2: Signal Engine
cd signal_engine && source .venv/bin/activate && python main.py

# Terminal 3: Execution Layer
cd execution_layer && cargo run --release

# Terminal 4: Orchestrator
cd orchestrator && dotnet run
```

Or use the provided scripts:
```bash
./scripts/run-all.sh    # Linux/macOS
.\scripts\run-all.ps1   # Windows
```

## API Documentation

### REST Endpoints

The C# Orchestrator exposes REST endpoints at `http://localhost:8080`.

Interactive API documentation is available at:
- **Swagger UI**: http://localhost:8080/swagger
- **OpenAPI JSON**: http://localhost:8080/swagger/v1/swagger.json

#### Health & Readiness

| Endpoint | Method | Description |
|----------|--------|-------------|
| `/health` | GET | Basic health check |
| `/ready` | GET | Readiness check with dependency status |
| `/metrics` | GET | Prometheus metrics |

#### Portfolio

| Endpoint | Method | Description |
|----------|--------|-------------|
| `/portfolio` | GET | Current portfolio summary with positions and P&L |

**Example Response:**
```json
{
  "cashBalance": 95000.00,
  "totalMarketValue": 5250.00,
  "totalEquity": 100250.00,
  "realizedPnL": 150.00,
  "unrealizedPnL": 100.00,
  "peakEquity": 100500.00,
  "positions": [
    {
      "asset": "BTC",
      "quantity": 0.1,
      "averageCost": 50000.00,
      "currentPrice": 52500.00,
      "marketValue": 5250.00,
      "unrealizedPnL": 250.00,
      "unrealizedPnLPercent": 0.05
    }
  ]
}
```

#### Trades

| Endpoint | Method | Description |
|----------|--------|-------------|
| `/trades` | GET | Paginated trade history |
| `/trades/{id}` | GET | Get specific trade by ID |

Query parameters for `/trades`:
- `page` (default: 1)
- `pageSize` (default: 20, max: 100)

#### Risk Management

| Endpoint | Method | Description |
|----------|--------|-------------|
| `/risk/limits` | GET | Current risk limits |
| `/risk/limits` | PUT | Update risk limits |

**Example PUT body:**
```json
{
  "maxDrawdownPercent": 0.05,
  "maxPositionSizePercent": 0.10,
  "maxExposurePercent": 0.80,
  "minOrderValue": 10.0
}
```

#### Trading Control

| Endpoint | Method | Description |
|----------|--------|-------------|
| `/trading/status` | GET | Current trading status |
| `/trading/pause` | POST | Pause all trading |
| `/trading/resume` | POST | Resume trading |

### gRPC Services

Defined in `shared/quantflow.proto`:

#### SignalService (Python - Port 50051)

```protobuf
service SignalService {
    rpc GetSignal(stream PriceTick) returns (TradeSignal);
}
```

#### ExecutionService (Rust - Port 50052)

```protobuf
service ExecutionService {
    rpc ExecuteOrder(OrderRequest) returns (ExecutionReceipt);
}
```

## Configuration Reference

### Environment Variables

#### PostgreSQL

| Variable | Default | Description |
|----------|---------|-------------|
| `POSTGRES_USER` | `postgres` | Database username |
| `POSTGRES_PASSWORD` | **Required** | Database password |
| `POSTGRES_DB` | `quantflow` | Database name |
| `POSTGRES_HOST` | `postgres` | Database host |
| `POSTGRES_PORT` | `5432` | Database port |

#### C# Orchestrator

| Variable | Default | Description |
|----------|---------|-------------|
| `ASPNETCORE_ENVIRONMENT` | `Production` | Runtime environment |
| `ASPNETCORE_URLS` | `http://+:8080` | HTTP binding |
| `SERILOG_MINIMUM_LEVEL` | `Information` | Log level |
| `SIGNAL_SERVICE_ADDRESS` | `http://signal_engine:50051` | Signal gRPC endpoint |
| `EXECUTION_SERVICE_ADDRESS` | `http://execution_layer:50052` | Execution gRPC endpoint |
| `PRICE_TICKER_INTERVAL_MS` | `1000` | Price tick interval |
| `PORTFOLIO_INITIAL_CASH` | `100000` | Starting cash balance |
| `RISK_MAX_DRAWDOWN_PERCENT` | `0.05` | Maximum allowed drawdown (5%) |
| `RISK_MAX_POSITION_SIZE_PERCENT` | `0.10` | Max single position size (10%) |
| `RISK_MAX_EXPOSURE_PERCENT` | `0.80` | Max total exposure (80%) |
| `RISK_MIN_ORDER_VALUE` | `10` | Minimum order value |

#### Python Signal Engine

| Variable | Default | Description |
|----------|---------|-------------|
| `SIGNAL_ENGINE_PORT` | `50051` | gRPC server port |
| `SIGNAL_ENGINE_HEALTH_PORT` | `8080` | Health check HTTP port |
| `SIGNAL_ENGINE_WINDOW_SIZE` | `20` | Rolling window size for MA |
| `SIGNAL_ENGINE_THRESHOLD` | `0.02` | Signal threshold (2%) |
| `SIGNAL_ENGINE_LOG_LEVEL` | `INFO` | Log level |

#### Rust Execution Layer

| Variable | Default | Description |
|----------|---------|-------------|
| `EXECUTION_SERVICE_PORT` | `50052` | gRPC server port |
| `EXECUTION_HEALTH_PORT` | `8081` | Health check HTTP port |
| `EXECUTION_MIN_LATENCY_MS` | `10` | Minimum simulated latency |
| `EXECUTION_MAX_LATENCY_MS` | `50` | Maximum simulated latency |
| `RUST_LOG` | `info` | Log level |

## Metrics & Monitoring

All services expose Prometheus-compatible metrics. See [METRICS.md](./METRICS.md) for full documentation.

### Metrics Endpoints

| Service | Endpoint | Port |
|---------|----------|------|
| Orchestrator | `/metrics` | 8080 |
| Signal Engine | `/metrics` | 8080 |
| Execution Layer | `/metrics` | 8081 |

### Key Metrics

- `quantflow_trades_executed_total` - Trade execution counter
- `quantflow_risk_rejections_total` - Risk rejection counter
- `quantflow_order_latency_seconds` - Order latency histogram
- `signal_engine_signals_generated_total` - Signal generation counter
- `execution_layer_execution_latency_seconds` - Execution latency histogram

## Testing

### C# Unit Tests

```bash
cd orchestrator.Tests
dotnet test
```

### C# Integration Tests

```bash
cd orchestrator.IntegrationTests
dotnet test
```

### Python Tests

```bash
cd signal_engine
source .venv/bin/activate
pytest tests/ -v
```

### Rust Tests

```bash
cd execution_layer
cargo test
```

### Run All Tests

```bash
# From project root
dotnet test orchestrator.Tests/
dotnet test orchestrator.IntegrationTests/
cd signal_engine && source .venv/bin/activate && pytest
cd execution_layer && cargo test
```

## Project Structure

```
quantflow/
├── shared/
│   └── quantflow.proto          # gRPC service definitions
├── orchestrator/                # C# .NET 10 Web API
│   ├── Program.cs               # Application entry point
│   ├── Data/
│   │   └── AppDbContext.cs      # EF Core database context
│   ├── Services/
│   │   ├── RiskManager.cs       # Risk limit enforcement
│   │   ├── PortfolioService.cs  # Portfolio state management
│   │   └── TradingControlService.cs
│   ├── Workers/
│   │   ├── PriceTickerWorker.cs # Price simulation
│   │   └── TradingOrchestrator.cs
│   ├── Clients/                 # gRPC clients
│   ├── Api/                     # REST endpoint DTOs
│   └── Dockerfile
├── signal_engine/               # Python 3 gRPC service
│   ├── main.py                  # gRPC server + signal logic
│   ├── requirements.txt
│   ├── tests/
│   └── Dockerfile
├── execution_layer/             # Rust async execution
│   ├── src/main.rs              # Tonic gRPC server
│   ├── Cargo.toml
│   ├── build.rs
│   └── Dockerfile
├── orchestrator.Tests/          # C# unit tests
├── orchestrator.IntegrationTests/ # C# integration tests
├── scripts/
│   ├── setup-dev.sh             # Development setup (Unix)
│   ├── setup-dev.ps1            # Development setup (Windows)
│   ├── run-all.sh               # Run all services
│   └── seed-data.sh             # Database seeding
├── docker-compose.yml
├── .env.example
└── METRICS.md
```

## Troubleshooting

### Common Issues

#### "Connection refused" to gRPC services

**Cause:** Services not started or wrong addresses.

**Solution:**
1. Check all services are running
2. Verify addresses in `.env`
3. Check Docker network connectivity

```bash
# Check service health
curl http://localhost:8080/ready
```

#### PostgreSQL connection failures

**Cause:** Database not running or wrong credentials.

**Solution:**
```bash
# Check PostgreSQL is running
docker-compose ps postgres

# Verify connection
docker-compose exec postgres pg_isready

# Check logs
docker-compose logs postgres
```

#### Python protobuf import errors

**Cause:** Stubs not generated.

**Solution:**
```bash
cd signal_engine
source .venv/bin/activate
python -m grpc_tools.protoc -I../shared \
    --python_out=. --grpc_python_out=. \
    ../shared/quantflow.proto
```

#### Rust build failures

**Cause:** Missing protoc or build dependencies.

**Solution:**
```bash
# Install protoc on macOS
brew install protobuf

# Install protoc on Ubuntu
sudo apt install protobuf-compiler

# Clean and rebuild
cd execution_layer
cargo clean
cargo build
```

#### .NET 10 SDK not found

**Cause:** Preview SDK not installed.

**Solution:**
Download .NET 10 Preview from [dotnet.microsoft.com/download/dotnet/10.0](https://dotnet.microsoft.com/download/dotnet/10.0)

#### Docker build fails on M1/M2 Mac

**Cause:** ARM64 compatibility issues.

**Solution:**
```bash
# Build with platform specification
docker-compose build --build-arg PLATFORM=linux/arm64
```

### Debug Mode

Enable verbose logging:

```bash
# C# Orchestrator
SERILOG_MINIMUM_LEVEL=Debug dotnet run

# Python Signal Engine
SIGNAL_ENGINE_LOG_LEVEL=DEBUG python main.py

# Rust Execution Layer
RUST_LOG=debug cargo run
```

### Health Check Endpoints

| Service | Endpoint | Healthy Response |
|---------|----------|------------------|
| Orchestrator | `http://localhost:8080/health` | `{"status":"Healthy"}` |
| Signal Engine | `http://localhost:8080/health` | `{"status":"Healthy"}` |
| Execution Layer | `http://localhost:8081/health` | `{"status":"Healthy"}` |

### Logs

```bash
# Docker Compose logs
docker-compose logs -f orchestrator
docker-compose logs -f signal_engine
docker-compose logs -f execution_layer

# All services
docker-compose logs -f
```

## Database Migrations

```bash
cd orchestrator

# Create a new migration
dotnet ef migrations add MigrationName

# Apply migrations
dotnet ef database update

# Revert last migration
dotnet ef database update PreviousMigrationName
```

## Seeding Sample Data

```bash
# Using the orchestrator CLI
cd orchestrator
dotnet run -- --seed

# Or via script
./scripts/seed-data.sh
```

## Contributing

1. Fork the repository
2. Create a feature branch
3. Run all tests before committing
4. Submit a pull request

## License

This project is licensed under the MIT License - see the [LICENSE](./LICENSE) file for details.
