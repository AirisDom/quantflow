#Requires -Version 7.0
param(
    [switch]$SkipBuild,
    [switch]$SignalOnly,
    [switch]$ExecutionOnly,
    [switch]$OrchestratorOnly,
    [switch]$Help
)

$ErrorActionPreference = "Stop"

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$ProjectRoot = Split-Path -Parent $ScriptDir

function Write-Info { param($Message) Write-Host "[INFO] $Message" -ForegroundColor Cyan }
function Write-Success { param($Message) Write-Host "[SUCCESS] $Message" -ForegroundColor Green }
function Write-Warn { param($Message) Write-Host "[WARN] $Message" -ForegroundColor Yellow }
function Write-Err { param($Message) Write-Host "[ERROR] $Message" -ForegroundColor Red }

if ($Help) {
    Write-Host @"
Usage: .\run-all.ps1 [OPTIONS]

Start all QuantFlow services locally without Docker.

Options:
  -Help               Show this help message
  -SkipBuild          Skip building services before starting
  -SignalOnly         Start only the Signal Engine
  -ExecutionOnly      Start only the Execution Layer
  -OrchestratorOnly   Start only the Orchestrator

Environment Variables:
  POSTGRES_HOST       PostgreSQL host (default: localhost)
  POSTGRES_PORT       PostgreSQL port (default: 5432)
  POSTGRES_USER       PostgreSQL user (default: postgres)
  POSTGRES_PASSWORD   PostgreSQL password (required for orchestrator)
  POSTGRES_DB         PostgreSQL database (default: quantflow)

Prerequisites:
  - PostgreSQL running locally or accessible
  - Run .\scripts\setup-dev.ps1 first to set up the environment
"@
    exit 0
}

$StartSignal = -not ($ExecutionOnly -or $OrchestratorOnly)
$StartExecution = -not ($SignalOnly -or $OrchestratorOnly)
$StartOrchestrator = -not ($SignalOnly -or $ExecutionOnly)

$envFile = Join-Path $ProjectRoot ".env"
if (Test-Path $envFile) {
    Write-Info "Loading environment from .env file..."
    Get-Content $envFile | ForEach-Object {
        if ($_ -match '^\s*([^#][^=]+)=(.*)$') {
            $name = $matches[1].Trim()
            $value = $matches[2].Trim()
            [Environment]::SetEnvironmentVariable($name, $value, "Process")
        }
    }
}

$env:POSTGRES_HOST = if ($env:POSTGRES_HOST) { $env:POSTGRES_HOST } else { "localhost" }
$env:POSTGRES_PORT = if ($env:POSTGRES_PORT) { $env:POSTGRES_PORT } else { "5432" }
$env:POSTGRES_USER = if ($env:POSTGRES_USER) { $env:POSTGRES_USER } else { "postgres" }
$env:POSTGRES_DB = if ($env:POSTGRES_DB) { $env:POSTGRES_DB } else { "quantflow" }

$SignalEnginePort = if ($env:SIGNAL_ENGINE_PORT) { $env:SIGNAL_ENGINE_PORT } else { "50051" }
$SignalHealthPort = if ($env:SIGNAL_ENGINE_HEALTH_PORT) { $env:SIGNAL_ENGINE_HEALTH_PORT } else { "8082" }
$ExecutionPort = if ($env:EXECUTION_SERVICE_PORT) { $env:EXECUTION_SERVICE_PORT } else { "50052" }
$ExecutionHealthPort = if ($env:EXECUTION_HEALTH_PORT) { $env:EXECUTION_HEALTH_PORT } else { "8083" }
$OrchestratorPort = if ($env:ORCHESTRATOR_PORT) { $env:ORCHESTRATOR_PORT } else { "8080" }

Write-Host ""
Write-Host "=============================================="
Write-Host "  QuantFlow Local Development Runner"
Write-Host "=============================================="
Write-Host ""

if ($StartOrchestrator -and -not $env:POSTGRES_PASSWORD) {
    Write-Err "POSTGRES_PASSWORD is required for the orchestrator"
    Write-Info "Set it in .env file or set `$env:POSTGRES_PASSWORD"
    exit 1
}

$logsDir = Join-Path $ProjectRoot "logs"
if (-not (Test-Path $logsDir)) {
    New-Item -ItemType Directory -Path $logsDir | Out-Null
}

$jobs = @()

try {
    if (-not $SkipBuild) {
        Write-Info "Building services..."

        if ($StartSignal) {
            Write-Info "Generating Python protobuf stubs..."
            Push-Location (Join-Path $ProjectRoot "signal_engine")
            & .\.venv\Scripts\Activate.ps1
            python -m grpc_tools.protoc `
                -I../shared `
                --python_out=. `
                --grpc_python_out=. `
                ../shared/quantflow.proto
            deactivate
            Pop-Location
            Write-Success "Python stubs generated"
        }

        if ($StartOrchestrator) {
            Write-Info "Building C# Orchestrator..."
            Push-Location (Join-Path $ProjectRoot "orchestrator")
            dotnet build -q
            Pop-Location
            Write-Success "C# Orchestrator built"
        }

        if ($StartExecution) {
            Write-Info "Building Rust Execution Layer..."
            Push-Location (Join-Path $ProjectRoot "execution_layer")
            cargo build --release 2>&1 | Select-Object -Last 3
            Pop-Location
            Write-Success "Rust Execution Layer built"
        }
        Write-Host ""
    }

    if ($StartSignal) {
        Write-Info "Starting Python Signal Engine on port $SignalEnginePort..."

        $signalLogPath = Join-Path $logsDir "signal_engine.log"
        $signalJob = Start-Job -ScriptBlock {
            param($ProjectRoot, $Port, $HealthPort, $LogPath)
            Set-Location (Join-Path $ProjectRoot "signal_engine")
            $env:SIGNAL_ENGINE_PORT = $Port
            $env:SIGNAL_ENGINE_HEALTH_PORT = $HealthPort
            & .\.venv\Scripts\python.exe main.py 2>&1 | Tee-Object -FilePath $LogPath
        } -ArgumentList $ProjectRoot, $SignalEnginePort, $SignalHealthPort, $signalLogPath

        $jobs += $signalJob
        Start-Sleep -Seconds 2

        if ($signalJob.State -eq "Running") {
            Write-Success "Signal Engine started (Job ID: $($signalJob.Id))"
        } else {
            Write-Err "Signal Engine failed to start. Check logs/signal_engine.log"
            throw "Signal Engine failed"
        }
    }

    if ($StartExecution) {
        Write-Info "Starting Rust Execution Layer on port $ExecutionPort..."

        $executionLogPath = Join-Path $logsDir "execution_layer.log"
        $executionJob = Start-Job -ScriptBlock {
            param($ProjectRoot, $Port, $HealthPort, $LogPath)
            Set-Location (Join-Path $ProjectRoot "execution_layer")
            $env:EXECUTION_SERVICE_PORT = $Port
            $env:EXECUTION_HEALTH_PORT = $HealthPort
            $env:RUST_LOG = "info"
            $exe = Join-Path $ProjectRoot "execution_layer\target\release\execution_layer.exe"
            if (Test-Path $exe) {
                & $exe 2>&1 | Tee-Object -FilePath $LogPath
            } else {
                cargo run --release 2>&1 | Tee-Object -FilePath $LogPath
            }
        } -ArgumentList $ProjectRoot, $ExecutionPort, $ExecutionHealthPort, $executionLogPath

        $jobs += $executionJob
        Start-Sleep -Seconds 3

        if ($executionJob.State -eq "Running") {
            Write-Success "Execution Layer started (Job ID: $($executionJob.Id))"
        } else {
            Write-Err "Execution Layer failed to start. Check logs/execution_layer.log"
            throw "Execution Layer failed"
        }
    }

    if ($StartOrchestrator) {
        Write-Info "Starting C# Orchestrator on port $OrchestratorPort..."

        $orchestratorLogPath = Join-Path $logsDir "orchestrator.log"
        $connStr = "Host=$($env:POSTGRES_HOST);Port=$($env:POSTGRES_PORT);Database=$($env:POSTGRES_DB);Username=$($env:POSTGRES_USER);Password=$($env:POSTGRES_PASSWORD)"

        $orchestratorJob = Start-Job -ScriptBlock {
            param($ProjectRoot, $Port, $ConnStr, $SignalPort, $ExecPort, $LogPath)
            Set-Location (Join-Path $ProjectRoot "orchestrator")
            $env:ASPNETCORE_ENVIRONMENT = "Development"
            $env:ASPNETCORE_URLS = "http://+:$Port"
            $env:ConnectionStrings__DefaultConnection = $ConnStr
            $env:SignalService__Address = "http://localhost:$SignalPort"
            $env:ExecutionService__Address = "http://localhost:$ExecPort"
            dotnet run --no-build 2>&1 | Tee-Object -FilePath $LogPath
        } -ArgumentList $ProjectRoot, $OrchestratorPort, $connStr, $SignalEnginePort, $ExecutionPort, $orchestratorLogPath

        $jobs += $orchestratorJob
        Start-Sleep -Seconds 4

        if ($orchestratorJob.State -eq "Running") {
            Write-Success "Orchestrator started (Job ID: $($orchestratorJob.Id))"
        } else {
            Write-Err "Orchestrator failed to start. Check logs/orchestrator.log"
            throw "Orchestrator failed"
        }
    }

    Write-Host ""
    Write-Host "=============================================="
    Write-Success "All requested services are running!"
    Write-Host "=============================================="
    Write-Host ""

    if ($StartSignal) {
        Write-Host "  Signal Engine:    grpc://localhost:$SignalEnginePort"
        Write-Host "                    http://localhost:$SignalHealthPort/health"
    }
    if ($StartExecution) {
        Write-Host "  Execution Layer:  grpc://localhost:$ExecutionPort"
        Write-Host "                    http://localhost:$ExecutionHealthPort/health"
    }
    if ($StartOrchestrator) {
        Write-Host "  Orchestrator:     http://localhost:$OrchestratorPort"
        Write-Host "                    http://localhost:$OrchestratorPort/swagger"
        Write-Host "                    http://localhost:$OrchestratorPort/health"
    }

    Write-Host ""
    Write-Host "Logs are written to: $logsDir"
    Write-Host ""
    Write-Info "Press Ctrl+C to stop all services"
    Write-Host ""

    while ($true) {
        $runningJobs = $jobs | Where-Object { $_.State -eq "Running" }
        if ($runningJobs.Count -eq 0) {
            Write-Warn "All services have stopped"
            break
        }
        Start-Sleep -Seconds 1
    }
}
finally {
    Write-Host ""
    Write-Info "Shutting down services..."

    foreach ($job in $jobs) {
        if ($job.State -eq "Running") {
            Write-Info "Stopping job $($job.Id)..."
            Stop-Job -Job $job -ErrorAction SilentlyContinue
            Remove-Job -Job $job -Force -ErrorAction SilentlyContinue
        }
    }

    Write-Success "All services stopped"
}
