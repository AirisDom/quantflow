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

check_command() {
    if command -v "$1" &> /dev/null; then
        log_success "$1 found: $(command -v "$1")"
        return 0
    else
        log_error "$1 not found"
        return 1
    fi
}

check_dotnet_version() {
    local required_major=10
    local version
    version=$(dotnet --version 2>/dev/null | head -1)
    local major
    major=$(echo "$version" | cut -d. -f1)

    if [[ "$major" -ge "$required_major" ]]; then
        log_success "dotnet version $version (>= $required_major.x required)"
        return 0
    else
        log_error "dotnet version $version found, but >= $required_major.x required"
        return 1
    fi
}

check_python_version() {
    local required_major=3
    local required_minor=10
    local version
    version=$(python3 --version 2>/dev/null | awk '{print $2}')
    local major
    major=$(echo "$version" | cut -d. -f1)
    local minor
    minor=$(echo "$version" | cut -d. -f2)

    if [[ "$major" -ge "$required_major" ]] && [[ "$minor" -ge "$required_minor" ]]; then
        log_success "Python version $version (>= $required_major.$required_minor required)"
        return 0
    else
        log_error "Python version $version found, but >= $required_major.$required_minor required"
        return 1
    fi
}

check_rust_version() {
    local version
    version=$(rustc --version 2>/dev/null | awk '{print $2}')
    log_success "Rust version $version"
    return 0
}

echo ""
echo "=============================================="
echo "  QuantFlow Development Environment Setup"
echo "=============================================="
echo ""

log_info "Checking prerequisites..."
echo ""

PREREQS_OK=true

log_info "Checking .NET SDK..."
if ! check_command dotnet; then
    log_error "Please install .NET 10 SDK from https://dotnet.microsoft.com/download"
    PREREQS_OK=false
elif ! check_dotnet_version; then
    log_error "Please upgrade to .NET 10 SDK"
    PREREQS_OK=false
fi

log_info "Checking Python..."
if ! check_command python3; then
    log_error "Please install Python 3.10+ from https://python.org/downloads"
    PREREQS_OK=false
elif ! check_python_version; then
    log_error "Please upgrade to Python 3.10+"
    PREREQS_OK=false
fi

log_info "Checking Rust..."
if ! check_command cargo; then
    log_error "Please install Rust from https://rustup.rs"
    PREREQS_OK=false
elif ! check_command rustc; then
    log_error "Please install Rust from https://rustup.rs"
    PREREQS_OK=false
else
    check_rust_version
fi

log_info "Checking protoc (Protocol Buffers compiler)..."
if ! check_command protoc; then
    log_warn "protoc not found - Python gRPC tools will be used instead"
fi

log_info "Checking Docker (optional for containerized development)..."
if check_command docker; then
    docker_version=$(docker --version 2>/dev/null || echo "unknown")
    log_info "Docker: $docker_version"
else
    log_warn "Docker not found - required only for containerized development"
fi

log_info "Checking docker-compose (optional)..."
if check_command docker-compose; then
    compose_version=$(docker-compose --version 2>/dev/null || echo "unknown")
    log_info "docker-compose: $compose_version"
elif docker compose version &> /dev/null; then
    log_success "docker compose (plugin) available"
else
    log_warn "docker-compose not found - required only for containerized development"
fi

echo ""

if [[ "$PREREQS_OK" != "true" ]]; then
    log_error "Some prerequisites are missing. Please install them and run this script again."
    exit 1
fi

log_success "All prerequisites satisfied!"
echo ""

log_info "Setting up environment file..."
if [[ ! -f "$PROJECT_ROOT/.env" ]]; then
    if [[ -f "$PROJECT_ROOT/.env.example" ]]; then
        cp "$PROJECT_ROOT/.env.example" "$PROJECT_ROOT/.env"
        log_success "Created .env from .env.example"
        log_warn "Please review and update .env with your local configuration"
    else
        log_warn ".env.example not found, skipping .env setup"
    fi
else
    log_info ".env already exists, skipping"
fi
echo ""

log_info "Setting up Python Signal Engine..."
cd "$PROJECT_ROOT/signal_engine"

if [[ ! -d ".venv" ]]; then
    log_info "Creating Python virtual environment..."
    python3 -m venv .venv
    log_success "Virtual environment created"
fi

log_info "Activating virtual environment and installing dependencies..."
source .venv/bin/activate
pip install --upgrade pip -q
pip install -r requirements.txt -q
log_success "Python dependencies installed"

log_info "Generating Python protobuf stubs..."
python -m grpc_tools.protoc \
    -I../shared \
    --python_out=. \
    --grpc_python_out=. \
    ../shared/quantflow.proto
log_success "Python protobuf stubs generated"

deactivate
cd "$PROJECT_ROOT"
echo ""

log_info "Setting up C# Orchestrator..."
cd "$PROJECT_ROOT/orchestrator"

log_info "Restoring NuGet packages..."
dotnet restore -q
log_success "NuGet packages restored"

log_info "Building C# orchestrator..."
if dotnet build -q --no-restore; then
    log_success "C# orchestrator built successfully"
else
    log_error "C# orchestrator build failed"
    exit 1
fi

log_info "Note: C# protobuf stubs are auto-generated during build via Grpc.Tools"

cd "$PROJECT_ROOT"
echo ""

log_info "Setting up Rust Execution Layer..."
cd "$PROJECT_ROOT/execution_layer"

log_info "Building Rust execution layer (this may take a while on first run)..."
if cargo build 2>&1 | tail -5; then
    log_success "Rust execution layer built successfully"
else
    log_error "Rust execution layer build failed"
    exit 1
fi

log_info "Note: Rust protobuf stubs are auto-generated during build via tonic-build"

cd "$PROJECT_ROOT"
echo ""

log_info "Building C# test projects..."
if [[ -d "$PROJECT_ROOT/orchestrator.Tests" ]]; then
    cd "$PROJECT_ROOT/orchestrator.Tests"
    dotnet restore -q
    if dotnet build -q --no-restore; then
        log_success "Unit tests project built"
    else
        log_warn "Unit tests project build failed"
    fi
    cd "$PROJECT_ROOT"
fi

if [[ -d "$PROJECT_ROOT/orchestrator.IntegrationTests" ]]; then
    cd "$PROJECT_ROOT/orchestrator.IntegrationTests"
    dotnet restore -q
    if dotnet build -q --no-restore; then
        log_success "Integration tests project built"
    else
        log_warn "Integration tests project build failed"
    fi
    cd "$PROJECT_ROOT"
fi
echo ""

log_info "Verifying all builds..."
echo ""

VERIFY_OK=true

cd "$PROJECT_ROOT/orchestrator"
if dotnet build --no-restore -q 2>/dev/null; then
    log_success "✓ C# Orchestrator"
else
    log_error "✗ C# Orchestrator"
    VERIFY_OK=false
fi

cd "$PROJECT_ROOT/signal_engine"
source .venv/bin/activate
if python -c "import quantflow_pb2; import quantflow_pb2_grpc; import main" 2>/dev/null; then
    log_success "✓ Python Signal Engine"
else
    log_error "✗ Python Signal Engine"
    VERIFY_OK=false
fi
deactivate

cd "$PROJECT_ROOT/execution_layer"
if cargo check -q 2>/dev/null; then
    log_success "✓ Rust Execution Layer"
else
    log_error "✗ Rust Execution Layer"
    VERIFY_OK=false
fi

cd "$PROJECT_ROOT"
echo ""

if [[ "$VERIFY_OK" == "true" ]]; then
    echo "=============================================="
    log_success "Development environment setup complete!"
    echo "=============================================="
    echo ""
    echo "Next steps:"
    echo "  1. Start PostgreSQL (or use Docker: docker-compose up postgres)"
    echo "  2. Run database migrations: cd orchestrator && dotnet ef database update"
    echo "  3. Start services with: ./scripts/run-all.sh"
    echo ""
    echo "Or use Docker Compose:"
    echo "  docker-compose up --build"
    echo ""
else
    log_error "Some builds failed. Please check the errors above."
    exit 1
fi
