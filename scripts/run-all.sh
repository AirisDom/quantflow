#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(dirname "$SCRIPT_DIR")"

RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m'

log_info() { echo -e "${BLUE}[INFO]${NC} $1"; }
log_success() { echo -e "${GREEN}[SUCCESS]${NC} $1"; }
log_warn() { echo -e "${YELLOW}[WARN]${NC} $1"; }
log_error() { echo -e "${RED}[ERROR]${NC} $1"; }

SIGNAL_ENGINE_PID=""
EXECUTION_LAYER_PID=""
ORCHESTRATOR_PID=""

cleanup() {
    echo ""
    log_info "Shutting down services..."

    if [[ -n "$ORCHESTRATOR_PID" ]] && kill -0 "$ORCHESTRATOR_PID" 2>/dev/null; then
        log_info "Stopping C# Orchestrator (PID: $ORCHESTRATOR_PID)..."
        kill -TERM "$ORCHESTRATOR_PID" 2>/dev/null || true
    fi

    if [[ -n "$EXECUTION_LAYER_PID" ]] && kill -0 "$EXECUTION_LAYER_PID" 2>/dev/null; then
        log_info "Stopping Rust Execution Layer (PID: $EXECUTION_LAYER_PID)..."
        kill -TERM "$EXECUTION_LAYER_PID" 2>/dev/null || true
    fi

    if [[ -n "$SIGNAL_ENGINE_PID" ]] && kill -0 "$SIGNAL_ENGINE_PID" 2>/dev/null; then
        log_info "Stopping Python Signal Engine (PID: $SIGNAL_ENGINE_PID)..."
        kill -TERM "$SIGNAL_ENGINE_PID" 2>/dev/null || true
    fi

    sleep 2

    for pid in "$ORCHESTRATOR_PID" "$EXECUTION_LAYER_PID" "$SIGNAL_ENGINE_PID"; do
        if [[ -n "$pid" ]] && kill -0 "$pid" 2>/dev/null; then
            log_warn "Force killing PID: $pid"
            kill -9 "$pid" 2>/dev/null || true
        fi
    done

    log_success "All services stopped"
    exit 0
}

trap cleanup SIGINT SIGTERM EXIT

show_help() {
    echo "Usage: $0 [OPTIONS]"
    echo ""
    echo "Start all QuantFlow services locally without Docker."
    echo ""
    echo "Options:"
    echo "  -h, --help              Show this help message"
    echo "  --skip-build            Skip building services before starting"
    echo "  --signal-only           Start only the Signal Engine"
    echo "  --execution-only        Start only the Execution Layer"
    echo "  --orchestrator-only     Start only the Orchestrator"
    echo ""
    echo "Environment Variables:"
    echo "  POSTGRES_HOST           PostgreSQL host (default: localhost)"
    echo "  POSTGRES_PORT           PostgreSQL port (default: 5432)"
    echo "  POSTGRES_USER           PostgreSQL user (default: postgres)"
    echo "  POSTGRES_PASSWORD       PostgreSQL password (required for orchestrator)"
    echo "  POSTGRES_DB             PostgreSQL database (default: quantflow)"
    echo ""
    echo "Prerequisites:"
    echo "  - PostgreSQL running locally or accessible"
    echo "  - Run ./scripts/setup-dev.sh first to set up the environment"
    echo ""
}

SKIP_BUILD=false
START_SIGNAL=true
START_EXECUTION=true
START_ORCHESTRATOR=true

while [[ $# -gt 0 ]]; do
    case $1 in
        -h|--help)
            show_help
            exit 0
            ;;
        --skip-build)
            SKIP_BUILD=true
            shift
            ;;
        --signal-only)
            START_EXECUTION=false
            START_ORCHESTRATOR=false
            shift
            ;;
        --execution-only)
            START_SIGNAL=false
            START_ORCHESTRATOR=false
            shift
            ;;
        --orchestrator-only)
            START_SIGNAL=false
            START_EXECUTION=false
            shift
            ;;
        *)
            log_error "Unknown option: $1"
            show_help
            exit 1
            ;;
    esac
done

if [[ -f "$PROJECT_ROOT/.env" ]]; then
    log_info "Loading environment from .env file..."
    set -a
    source "$PROJECT_ROOT/.env"
    set +a
fi

POSTGRES_HOST="${POSTGRES_HOST:-localhost}"
POSTGRES_PORT="${POSTGRES_PORT:-5432}"
POSTGRES_USER="${POSTGRES_USER:-postgres}"
POSTGRES_PASSWORD="${POSTGRES_PASSWORD:-}"
POSTGRES_DB="${POSTGRES_DB:-quantflow}"

SIGNAL_ENGINE_PORT="${SIGNAL_ENGINE_PORT:-50051}"
SIGNAL_ENGINE_HEALTH_PORT="${SIGNAL_ENGINE_HEALTH_PORT:-8082}"
EXECUTION_SERVICE_PORT="${EXECUTION_SERVICE_PORT:-50052}"
EXECUTION_HEALTH_PORT="${EXECUTION_HEALTH_PORT:-8083}"
ORCHESTRATOR_PORT="${ORCHESTRATOR_PORT:-8080}"

echo ""
echo "=============================================="
echo "  QuantFlow Local Development Runner"
echo "=============================================="
echo ""

if [[ "$START_ORCHESTRATOR" == "true" ]] && [[ -z "$POSTGRES_PASSWORD" ]]; then
    log_error "POSTGRES_PASSWORD is required for the orchestrator"
    log_info "Set it in .env file or export POSTGRES_PASSWORD=yourpassword"
    exit 1
fi

mkdir -p "$PROJECT_ROOT/logs"

if [[ "$SKIP_BUILD" != "true" ]]; then
    log_info "Building services..."

    if [[ "$START_SIGNAL" == "true" ]]; then
        log_info "Generating Python protobuf stubs..."
        cd "$PROJECT_ROOT/signal_engine"
        source .venv/bin/activate
        python -m grpc_tools.protoc \
            -I../shared \
            --python_out=. \
            --grpc_python_out=. \
            ../shared/quantflow.proto
        deactivate
        log_success "Python stubs generated"
    fi

    if [[ "$START_ORCHESTRATOR" == "true" ]]; then
        log_info "Building C# Orchestrator..."
        cd "$PROJECT_ROOT/orchestrator"
        dotnet build -q
        log_success "C# Orchestrator built"
    fi

    if [[ "$START_EXECUTION" == "true" ]]; then
        log_info "Building Rust Execution Layer..."
        cd "$PROJECT_ROOT/execution_layer"
        cargo build --release -q 2>/dev/null || cargo build --release
        log_success "Rust Execution Layer built"
    fi

    cd "$PROJECT_ROOT"
    echo ""
fi

if [[ "$START_SIGNAL" == "true" ]]; then
    log_info "Starting Python Signal Engine on port $SIGNAL_ENGINE_PORT..."
    cd "$PROJECT_ROOT/signal_engine"
    source .venv/bin/activate

    export SIGNAL_ENGINE_PORT="$SIGNAL_ENGINE_PORT"
    export SIGNAL_ENGINE_HEALTH_PORT="$SIGNAL_ENGINE_HEALTH_PORT"
    export SIGNAL_ENGINE_LOG_LEVEL="${SIGNAL_ENGINE_LOG_LEVEL:-INFO}"

    python main.py > "$PROJECT_ROOT/logs/signal_engine.log" 2>&1 &
    SIGNAL_ENGINE_PID=$!

    deactivate
    cd "$PROJECT_ROOT"

    sleep 1
    if kill -0 "$SIGNAL_ENGINE_PID" 2>/dev/null; then
        log_success "Signal Engine started (PID: $SIGNAL_ENGINE_PID)"
    else
        log_error "Signal Engine failed to start. Check logs/signal_engine.log"
        exit 1
    fi
fi

if [[ "$START_EXECUTION" == "true" ]]; then
    log_info "Starting Rust Execution Layer on port $EXECUTION_SERVICE_PORT..."
    cd "$PROJECT_ROOT/execution_layer"

    export EXECUTION_SERVICE_PORT="$EXECUTION_SERVICE_PORT"
    export EXECUTION_HEALTH_PORT="$EXECUTION_HEALTH_PORT"
    export RUST_LOG="${RUST_LOG:-info}"

    if [[ -f "target/release/execution_layer" ]]; then
        ./target/release/execution_layer > "$PROJECT_ROOT/logs/execution_layer.log" 2>&1 &
    else
        cargo run --release > "$PROJECT_ROOT/logs/execution_layer.log" 2>&1 &
    fi
    EXECUTION_LAYER_PID=$!

    cd "$PROJECT_ROOT"

    sleep 2
    if kill -0 "$EXECUTION_LAYER_PID" 2>/dev/null; then
        log_success "Execution Layer started (PID: $EXECUTION_LAYER_PID)"
    else
        log_error "Execution Layer failed to start. Check logs/execution_layer.log"
        exit 1
    fi
fi

if [[ "$START_ORCHESTRATOR" == "true" ]]; then
    log_info "Starting C# Orchestrator on port $ORCHESTRATOR_PORT..."
    cd "$PROJECT_ROOT/orchestrator"

    export ASPNETCORE_ENVIRONMENT="${ASPNETCORE_ENVIRONMENT:-Development}"
    export ASPNETCORE_URLS="http://+:$ORCHESTRATOR_PORT"
    export ConnectionStrings__DefaultConnection="Host=$POSTGRES_HOST;Port=$POSTGRES_PORT;Database=$POSTGRES_DB;Username=$POSTGRES_USER;Password=$POSTGRES_PASSWORD"
    export SignalService__Address="http://localhost:$SIGNAL_ENGINE_PORT"
    export ExecutionService__Address="http://localhost:$EXECUTION_SERVICE_PORT"

    dotnet run --no-build > "$PROJECT_ROOT/logs/orchestrator.log" 2>&1 &
    ORCHESTRATOR_PID=$!

    cd "$PROJECT_ROOT"

    sleep 3
    if kill -0 "$ORCHESTRATOR_PID" 2>/dev/null; then
        log_success "Orchestrator started (PID: $ORCHESTRATOR_PID)"
    else
        log_error "Orchestrator failed to start. Check logs/orchestrator.log"
        exit 1
    fi
fi

echo ""
echo "=============================================="
log_success "All requested services are running!"
echo "=============================================="
echo ""

if [[ "$START_SIGNAL" == "true" ]]; then
    echo "  Signal Engine:    grpc://localhost:$SIGNAL_ENGINE_PORT"
    echo "                    http://localhost:$SIGNAL_ENGINE_HEALTH_PORT/health"
fi
if [[ "$START_EXECUTION" == "true" ]]; then
    echo "  Execution Layer:  grpc://localhost:$EXECUTION_SERVICE_PORT"
    echo "                    http://localhost:$EXECUTION_HEALTH_PORT/health"
fi
if [[ "$START_ORCHESTRATOR" == "true" ]]; then
    echo "  Orchestrator:     http://localhost:$ORCHESTRATOR_PORT"
    echo "                    http://localhost:$ORCHESTRATOR_PORT/swagger"
    echo "                    http://localhost:$ORCHESTRATOR_PORT/health"
fi

echo ""
echo "Logs are written to: $PROJECT_ROOT/logs/"
echo ""
log_info "Press Ctrl+C to stop all services"
echo ""

wait
