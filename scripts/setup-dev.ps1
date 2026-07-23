#Requires -Version 7.0
$ErrorActionPreference = "Stop"

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$ProjectRoot = Split-Path -Parent $ScriptDir

function Write-Info { param($Message) Write-Host "[INFO] $Message" -ForegroundColor Cyan }
function Write-Success { param($Message) Write-Host "[SUCCESS] $Message" -ForegroundColor Green }
function Write-Warn { param($Message) Write-Host "[WARN] $Message" -ForegroundColor Yellow }
function Write-Err { param($Message) Write-Host "[ERROR] $Message" -ForegroundColor Red }

function Test-Command {
    param($Command)
    $null = Get-Command $Command -ErrorAction SilentlyContinue
    return $?
}

function Test-DotNetVersion {
    $version = dotnet --version
    $major = [int]($version -split '\.')[0]
    if ($major -ge 10) {
        Write-Success "dotnet version $version (>= 10.x required)"
        return $true
    }
    Write-Err "dotnet version $version found, but >= 10.x required"
    return $false
}

function Test-PythonVersion {
    $version = (python --version 2>&1) -replace 'Python ', ''
    $parts = $version -split '\.'
    $major = [int]$parts[0]
    $minor = [int]$parts[1]
    if ($major -ge 3 -and $minor -ge 10) {
        Write-Success "Python version $version (>= 3.10 required)"
        return $true
    }
    Write-Err "Python version $version found, but >= 3.10 required"
    return $false
}

Write-Host ""
Write-Host "=============================================="
Write-Host "  QuantFlow Development Environment Setup"
Write-Host "=============================================="
Write-Host ""

Write-Info "Checking prerequisites..."
Write-Host ""

$prereqsOk = $true

Write-Info "Checking .NET SDK..."
if (-not (Test-Command "dotnet")) {
    Write-Err "dotnet not found. Please install .NET 10 SDK from https://dotnet.microsoft.com/download"
    $prereqsOk = $false
} elseif (-not (Test-DotNetVersion)) {
    Write-Err "Please upgrade to .NET 10 SDK"
    $prereqsOk = $false
}

Write-Info "Checking Python..."
if (-not (Test-Command "python")) {
    Write-Err "python not found. Please install Python 3.10+ from https://python.org/downloads"
    $prereqsOk = $false
} elseif (-not (Test-PythonVersion)) {
    Write-Err "Please upgrade to Python 3.10+"
    $prereqsOk = $false
}

Write-Info "Checking Rust..."
if (-not (Test-Command "cargo")) {
    Write-Err "cargo not found. Please install Rust from https://rustup.rs"
    $prereqsOk = $false
} else {
    $rustVersion = rustc --version
    Write-Success "Rust: $rustVersion"
}

Write-Info "Checking protoc (optional)..."
if (Test-Command "protoc") {
    $protocVersion = protoc --version
    Write-Success "protoc: $protocVersion"
} else {
    Write-Warn "protoc not found - Python gRPC tools will be used instead"
}

Write-Host ""

if (-not $prereqsOk) {
    Write-Err "Some prerequisites are missing. Please install them and run this script again."
    exit 1
}

Write-Success "All prerequisites satisfied!"
Write-Host ""

Write-Info "Setting up environment file..."
$envPath = Join-Path $ProjectRoot ".env"
$envExamplePath = Join-Path $ProjectRoot ".env.example"
if (-not (Test-Path $envPath)) {
    if (Test-Path $envExamplePath) {
        Copy-Item $envExamplePath $envPath
        Write-Success "Created .env from .env.example"
        Write-Warn "Please review and update .env with your local configuration"
    } else {
        Write-Warn ".env.example not found, skipping .env setup"
    }
} else {
    Write-Info ".env already exists, skipping"
}
Write-Host ""

Write-Info "Setting up Python Signal Engine..."
Push-Location (Join-Path $ProjectRoot "signal_engine")

if (-not (Test-Path ".venv")) {
    Write-Info "Creating Python virtual environment..."
    python -m venv .venv
    Write-Success "Virtual environment created"
}

Write-Info "Activating virtual environment and installing dependencies..."
& .\.venv\Scripts\Activate.ps1
python -m pip install --upgrade pip -q
pip install -r requirements.txt -q
Write-Success "Python dependencies installed"

Write-Info "Generating Python protobuf stubs..."
python -m grpc_tools.protoc `
    -I../shared `
    --python_out=. `
    --grpc_python_out=. `
    ../shared/quantflow.proto
Write-Success "Python protobuf stubs generated"

deactivate
Pop-Location
Write-Host ""

Write-Info "Setting up C# Orchestrator..."
Push-Location (Join-Path $ProjectRoot "orchestrator")

Write-Info "Restoring NuGet packages..."
dotnet restore -q
Write-Success "NuGet packages restored"

Write-Info "Building C# orchestrator..."
$buildResult = dotnet build -q --no-restore 2>&1
if ($LASTEXITCODE -eq 0) {
    Write-Success "C# orchestrator built successfully"
} else {
    Write-Err "C# orchestrator build failed"
    Write-Host $buildResult
    exit 1
}

Pop-Location
Write-Host ""

Write-Info "Setting up Rust Execution Layer..."
Push-Location (Join-Path $ProjectRoot "execution_layer")

Write-Info "Building Rust execution layer (this may take a while on first run)..."
cargo build 2>&1 | Select-Object -Last 5
if ($LASTEXITCODE -eq 0) {
    Write-Success "Rust execution layer built successfully"
} else {
    Write-Err "Rust execution layer build failed"
    exit 1
}

Pop-Location
Write-Host ""

Write-Info "Building C# test projects..."
$testsPath = Join-Path $ProjectRoot "orchestrator.Tests"
if (Test-Path $testsPath) {
    Push-Location $testsPath
    dotnet restore -q
    $buildResult = dotnet build -q --no-restore 2>&1
    if ($LASTEXITCODE -eq 0) {
        Write-Success "Unit tests project built"
    } else {
        Write-Warn "Unit tests project build failed"
    }
    Pop-Location
}

$integrationTestsPath = Join-Path $ProjectRoot "orchestrator.IntegrationTests"
if (Test-Path $integrationTestsPath) {
    Push-Location $integrationTestsPath
    dotnet restore -q
    $buildResult = dotnet build -q --no-restore 2>&1
    if ($LASTEXITCODE -eq 0) {
        Write-Success "Integration tests project built"
    } else {
        Write-Warn "Integration tests project build failed"
    }
    Pop-Location
}
Write-Host ""

Write-Info "Verifying all builds..."
Write-Host ""

$verifyOk = $true

Push-Location (Join-Path $ProjectRoot "orchestrator")
$null = dotnet build --no-restore -q 2>&1
if ($LASTEXITCODE -eq 0) {
    Write-Success "✓ C# Orchestrator"
} else {
    Write-Err "✗ C# Orchestrator"
    $verifyOk = $false
}
Pop-Location

Push-Location (Join-Path $ProjectRoot "signal_engine")
& .\.venv\Scripts\Activate.ps1
try {
    python -c "import quantflow_pb2; import quantflow_pb2_grpc; import main" 2>&1 | Out-Null
    if ($LASTEXITCODE -eq 0) {
        Write-Success "✓ Python Signal Engine"
    } else {
        Write-Err "✗ Python Signal Engine"
        $verifyOk = $false
    }
} catch {
    Write-Err "✗ Python Signal Engine"
    $verifyOk = $false
}
deactivate
Pop-Location

Push-Location (Join-Path $ProjectRoot "execution_layer")
cargo check -q 2>&1 | Out-Null
if ($LASTEXITCODE -eq 0) {
    Write-Success "✓ Rust Execution Layer"
} else {
    Write-Err "✗ Rust Execution Layer"
    $verifyOk = $false
}
Pop-Location

Write-Host ""

if ($verifyOk) {
    Write-Host "=============================================="
    Write-Success "Development environment setup complete!"
    Write-Host "=============================================="
    Write-Host ""
    Write-Host "Next steps:"
    Write-Host "  1. Start PostgreSQL (or use Docker: docker-compose up postgres)"
    Write-Host "  2. Run database migrations: cd orchestrator; dotnet ef database update"
    Write-Host "  3. Start services with: .\scripts\run-all.ps1"
    Write-Host ""
    Write-Host "Or use Docker Compose:"
    Write-Host "  docker-compose up --build"
    Write-Host ""
} else {
    Write-Err "Some builds failed. Please check the errors above."
    exit 1
}
