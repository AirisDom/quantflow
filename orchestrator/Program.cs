using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi.Models;
using QuantFlow.Orchestrator.Api;
using QuantFlow.Orchestrator.Channels;
using QuantFlow.Orchestrator.Clients;
using QuantFlow.Orchestrator.Configuration;
using QuantFlow.Orchestrator.Data;
using QuantFlow.Orchestrator.Logging;
using QuantFlow.Orchestrator.Services;
using QuantFlow.Orchestrator.Shutdown;
using QuantFlow.Orchestrator.Workers;
using Serilog;
using Serilog.Formatting.Compact;

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .WriteTo.Console(new RenderedCompactJsonFormatter())
    .CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    builder.Host.UseSerilog((context, services, configuration) => configuration
        .ReadFrom.Configuration(context.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext()
        .Enrich.WithProperty("Service", "QuantFlow.Orchestrator")
        .Enrich.WithProperty("Version", typeof(Program).Assembly.GetName().Version?.ToString() ?? "1.0.0")
        .Enrich.WithMachineName()
        .Enrich.WithThreadId()
        .WriteTo.Console(new RenderedCompactJsonFormatter()));

    try
    {
        builder.Configuration.ValidateRequiredConfiguration();
    }
    catch (ConfigurationValidationException ex)
    {
        Log.Fatal(ex, "Configuration validation failed");
        Environment.Exit(1);
    }

    builder.Services.AddQuantFlowConfiguration(builder.Configuration);

    builder.Services.AddDbContext<AppDbContext>(options =>
        options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

    builder.Services.AddSingleton<IRiskManager, RiskManager>();
    builder.Services.AddSingleton<IPortfolioService, PortfolioService>();
    builder.Services.AddSingleton<ITradingControlService, TradingControlService>();
    builder.Services.AddSingleton<ISignalServiceClient, SignalServiceClient>();
    builder.Services.AddSingleton<IExecutionServiceClient, ExecutionServiceClient>();

    builder.Services.AddSingleton<IPriceTickChannel, PriceTickChannel>();
    builder.Services.AddSingleton<IGracefulShutdownService, GracefulShutdownService>();
    builder.Services.AddHostedService<PriceTickerWorker>();
    builder.Services.AddHostedService<TradingOrchestrator>();
    builder.Services.AddHostedService<GracefulShutdownHostedService>();

    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen(options =>
    {
        options.SwaggerDoc("v1", new OpenApiInfo
        {
            Version = "v1",
            Title = "QuantFlow Orchestrator API",
            Description = "A cloud-native algorithmic trading orchestrator that manages portfolio state, risk limits, and trade execution across Python and Rust microservices.",
            Contact = new OpenApiContact
            {
                Name = "QuantFlow Team",
                Email = "support@quantflow.io"
            },
            License = new OpenApiLicense
            {
                Name = "MIT",
                Url = new Uri("https://opensource.org/licenses/MIT")
            }
        });

        var xmlFilename = $"{Assembly.GetExecutingAssembly().GetName().Name}.xml";
        var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFilename);
        if (File.Exists(xmlPath))
        {
            options.IncludeXmlComments(xmlPath);
        }
    });

    var app = builder.Build();

    app.UseMiddleware<CorrelationIdMiddleware>();
    app.UseSerilogRequestLogging(options =>
    {
        options.EnrichDiagnosticContext = (diagnosticContext, httpContext) =>
        {
            diagnosticContext.Set("RequestHost", httpContext.Request.Host.Value ?? "unknown");
            diagnosticContext.Set("UserAgent", httpContext.Request.Headers.UserAgent.ToString() ?? "unknown");
            if (httpContext.Items.TryGetValue("CorrelationId", out var correlationId) && correlationId != null)
            {
                diagnosticContext.Set("CorrelationId", correlationId);
            }
        };
    });

    app.UseSwagger();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/swagger/v1/swagger.json", "QuantFlow Orchestrator API v1");
        options.RoutePrefix = "swagger";
        options.DocumentTitle = "QuantFlow API Documentation";
        options.EnableTryItOutByDefault();
    });

    if (app.Environment.IsDevelopment())
    {
        using var scope = app.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        dbContext.Database.Migrate();

        app.MapOpenApi();
    }

    app.UseHttpsRedirection();

    app.MapGet("/health", (IGracefulShutdownService shutdownService) =>
    {
        if (shutdownService.IsShuttingDown)
        {
            var shuttingDownResponse = new HealthResponse(
                Status: "ShuttingDown",
                Service: "QuantFlow.Orchestrator",
                Timestamp: DateTime.UtcNow
            );
            return Results.Json(shuttingDownResponse, statusCode: StatusCodes.Status503ServiceUnavailable);
        }
        var healthResponse = new HealthResponse(
            Status: "Healthy",
            Service: "QuantFlow.Orchestrator",
            Timestamp: DateTime.UtcNow
        );
        return Results.Ok(healthResponse);
    })
    .WithName("HealthCheck")
    .WithTags("Health")
    .WithSummary("Check service health")
    .WithDescription("Returns the current health status of the QuantFlow Orchestrator service.")
    .Produces<HealthResponse>(StatusCodes.Status200OK)
    .WithOpenApi();

    app.MapGet("/ready", async (
        IGracefulShutdownService shutdownService,
        AppDbContext db,
        ISignalServiceClient signalClient,
        IExecutionServiceClient executionClient,
        CancellationToken cancellationToken) =>
    {
        if (shutdownService.IsShuttingDown)
        {
            var shuttingDownResponse = new ReadyResponse(
                Status: "ShuttingDown",
                Service: "QuantFlow.Orchestrator",
                Timestamp: DateTime.UtcNow,
                Checks: []
            );
            return Results.Json(shuttingDownResponse, statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        var checks = new List<DependencyCheck>();
        var allHealthy = true;

        var dbStart = DateTime.UtcNow;
        bool dbHealthy;
        try
        {
            dbHealthy = await db.Database.CanConnectAsync(cancellationToken);
        }
        catch
        {
            dbHealthy = false;
        }
        var dbDuration = (long)(DateTime.UtcNow - dbStart).TotalMilliseconds;
        checks.Add(new DependencyCheck("PostgreSQL", dbHealthy ? "Healthy" : "Unhealthy", dbDuration));
        allHealthy &= dbHealthy;

        var signalStart = DateTime.UtcNow;
        var signalHealthy = await signalClient.CheckHealthAsync(cancellationToken);
        var signalDuration = (long)(DateTime.UtcNow - signalStart).TotalMilliseconds;
        checks.Add(new DependencyCheck("SignalService", signalHealthy ? "Healthy" : "Unhealthy", signalDuration));
        allHealthy &= signalHealthy;

        var execStart = DateTime.UtcNow;
        var execHealthy = await executionClient.CheckHealthAsync(cancellationToken);
        var execDuration = (long)(DateTime.UtcNow - execStart).TotalMilliseconds;
        checks.Add(new DependencyCheck("ExecutionService", execHealthy ? "Healthy" : "Unhealthy", execDuration));
        allHealthy &= execHealthy;

        var response = new ReadyResponse(
            Status: allHealthy ? "Ready" : "NotReady",
            Service: "QuantFlow.Orchestrator",
            Timestamp: DateTime.UtcNow,
            Checks: checks
        );

        return allHealthy
            ? Results.Ok(response)
            : Results.Json(response, statusCode: StatusCodes.Status503ServiceUnavailable);
    })
    .WithName("ReadinessCheck")
    .WithTags("Health")
    .WithSummary("Check service readiness")
    .WithDescription("Returns the readiness status including database and gRPC service connectivity.")
    .Produces<ReadyResponse>(StatusCodes.Status200OK)
    .Produces<ReadyResponse>(StatusCodes.Status503ServiceUnavailable)
    .WithOpenApi();

    app.MapGet("/portfolio", (IPortfolioService portfolioService) =>
    {
        var summary = portfolioService.GetSummary();
        var response = new PortfolioResponse(
            CashBalance: summary.CashBalance,
            TotalMarketValue: summary.TotalMarketValue,
            TotalEquity: summary.TotalEquity,
            RealizedPnL: summary.RealizedPnL,
            UnrealizedPnL: summary.UnrealizedPnL,
            PeakEquity: summary.PeakEquity,
            Positions: summary.Positions.Values.Select(p => new PositionResponse(
                Asset: p.Asset,
                Quantity: p.Quantity,
                AverageCost: p.AverageCost,
                CurrentPrice: p.CurrentPrice,
                MarketValue: p.MarketValue,
                UnrealizedPnL: p.UnrealizedPnL,
                UnrealizedPnLPercent: p.UnrealizedPnLPercent
            ))
        );
        return Results.Ok(response);
    })
    .WithName("GetPortfolio")
    .WithTags("Portfolio")
    .WithSummary("Get portfolio summary")
    .WithDescription("Returns the current portfolio state including cash balance, positions, and unrealized P&L.")
    .Produces<PortfolioResponse>(StatusCodes.Status200OK)
    .WithOpenApi();

    app.MapGet("/trades", async (AppDbContext db, int page = 1, int pageSize = 20) =>
    {
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 1;
        if (pageSize > 100) pageSize = 100;

        var totalCount = await db.TradeRecords.CountAsync();
        var totalPages = (int)Math.Ceiling(totalCount / (double)pageSize);

        var trades = await db.TradeRecords
            .OrderByDescending(t => t.Timestamp)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(t => new TradeResponse(
                Id: t.Id,
                OrderId: t.OrderId,
                Asset: t.Asset,
                Side: t.Side,
                Quantity: t.Quantity,
                Price: t.Price,
                Timestamp: t.Timestamp
            ))
            .ToListAsync();

        var response = new TradesPagedResponse(
            Page: page,
            PageSize: pageSize,
            TotalCount: totalCount,
            TotalPages: totalPages,
            Trades: trades
        );
        return Results.Ok(response);
    })
    .WithName("GetTrades")
    .WithTags("Trades")
    .WithSummary("Get paginated trade history")
    .WithDescription("Returns a paginated list of executed trades, ordered by timestamp descending. Supports pagination with page (default: 1) and pageSize (default: 20, max: 100) parameters.")
    .Produces<TradesPagedResponse>(StatusCodes.Status200OK)
    .WithOpenApi(op =>
    {
        if (op.Parameters is { Count: > 1 })
        {
            op.Parameters[0].Description = "Page number (minimum: 1)";
            op.Parameters[1].Description = "Number of trades per page (minimum: 1, maximum: 100)";
        }
        return op;
    });

    app.MapGet("/trades/{id:guid}", async (Guid id, AppDbContext db) =>
    {
        var trade = await db.TradeRecords.FindAsync(id);
        if (trade is null)
        {
            return Results.NotFound(new TradeNotFoundResponse(Error: "Trade not found", TradeId: id));
        }

        var response = new TradeResponse(
            Id: trade.Id,
            OrderId: trade.OrderId,
            Asset: trade.Asset,
            Side: trade.Side,
            Quantity: trade.Quantity,
            Price: trade.Price,
            Timestamp: trade.Timestamp
        );
        return Results.Ok(response);
    })
    .WithName("GetTradeById")
    .WithTags("Trades")
    .WithSummary("Get trade by ID")
    .WithDescription("Returns the details of a specific trade by its unique identifier.")
    .Produces<TradeResponse>(StatusCodes.Status200OK)
    .Produces<TradeNotFoundResponse>(StatusCodes.Status404NotFound)
    .WithOpenApi(op =>
    {
        if (op.Parameters is { Count: > 0 })
        {
            op.Parameters[0].Description = "The unique trade identifier (GUID)";
        }
        return op;
    });

    app.MapGet("/risk/limits", (IRiskManager riskManager) =>
    {
        var limits = riskManager.GetCurrentLimits();
        var response = new RiskLimitsResponse(
            MaxDrawdownPercent: limits.MaxDrawdownPercent,
            MaxPositionSizePercent: limits.MaxPositionSizePercent,
            MaxExposurePercent: limits.MaxExposurePercent,
            MinOrderValue: limits.MinOrderValue,
            MaxDrawdownPercentFormatted: $"{limits.MaxDrawdownPercent:P2}",
            MaxPositionSizePercentFormatted: $"{limits.MaxPositionSizePercent:P2}",
            MaxExposurePercentFormatted: $"{limits.MaxExposurePercent:P2}"
        );
        return Results.Ok(response);
    })
    .WithName("GetRiskLimits")
    .WithTags("Risk Management")
    .WithSummary("Get current risk limits")
    .WithDescription("Returns the current risk management configuration including maximum drawdown, position size, and exposure limits.")
    .Produces<RiskLimitsResponse>(StatusCodes.Status200OK)
    .WithOpenApi();

    app.MapPut("/risk/limits", (RiskLimitsUpdateRequest request, IRiskManager riskManager) =>
    {
        var errors = new List<string>();

        if (request.MaxDrawdownPercent.HasValue)
        {
            if (request.MaxDrawdownPercent.Value < 0 || request.MaxDrawdownPercent.Value > 1)
                errors.Add("MaxDrawdownPercent must be between 0 and 1");
        }
        if (request.MaxPositionSizePercent.HasValue)
        {
            if (request.MaxPositionSizePercent.Value < 0 || request.MaxPositionSizePercent.Value > 1)
                errors.Add("MaxPositionSizePercent must be between 0 and 1");
        }
        if (request.MaxExposurePercent.HasValue)
        {
            if (request.MaxExposurePercent.Value < 0 || request.MaxExposurePercent.Value > 1)
                errors.Add("MaxExposurePercent must be between 0 and 1");
        }
        if (request.MinOrderValue.HasValue)
        {
            if (request.MinOrderValue.Value < 0)
                errors.Add("MinOrderValue must be non-negative");
        }

        if (errors.Count > 0)
        {
            return Results.BadRequest(new ValidationErrorResponse(Errors: errors));
        }

        var updatedLimits = riskManager.UpdateLimits(request);
        var limitsResponse = new RiskLimitsResponse(
            MaxDrawdownPercent: updatedLimits.MaxDrawdownPercent,
            MaxPositionSizePercent: updatedLimits.MaxPositionSizePercent,
            MaxExposurePercent: updatedLimits.MaxExposurePercent,
            MinOrderValue: updatedLimits.MinOrderValue,
            MaxDrawdownPercentFormatted: $"{updatedLimits.MaxDrawdownPercent:P2}",
            MaxPositionSizePercentFormatted: $"{updatedLimits.MaxPositionSizePercent:P2}",
            MaxExposurePercentFormatted: $"{updatedLimits.MaxExposurePercent:P2}"
        );
        var response = new RiskLimitsUpdateResponse(
            Message: "Risk limits updated successfully",
            Limits: limitsResponse
        );
        return Results.Ok(response);
    })
    .WithName("UpdateRiskLimits")
    .WithTags("Risk Management")
    .WithSummary("Update risk limits")
    .WithDescription("Updates one or more risk management limits. All values are optional; only provided values will be updated. Percentages should be provided as decimals (e.g., 0.05 for 5%).")
    .Accepts<RiskLimitsUpdateRequest>("application/json")
    .Produces<RiskLimitsUpdateResponse>(StatusCodes.Status200OK)
    .Produces<ValidationErrorResponse>(StatusCodes.Status400BadRequest)
    .WithOpenApi();

    app.MapGet("/trading/status", (ITradingControlService tradingControl) =>
    {
        var status = tradingControl.GetStatus();
        var response = new TradingStatusResponse(
            IsTradingEnabled: status.IsTradingEnabled,
            Status: status.IsTradingEnabled ? "Active" : "Paused",
            PausedAt: status.PausedAt,
            PausedBy: status.PausedBy,
            PauseReason: status.PauseReason
        );
        return Results.Ok(response);
    })
    .WithName("GetTradingStatus")
    .WithTags("Trading Control")
    .WithSummary("Get trading status")
    .WithDescription("Returns the current trading status including whether trading is active or paused, and pause details if applicable.")
    .Produces<TradingStatusResponse>(StatusCodes.Status200OK)
    .WithOpenApi();

    app.MapPost("/trading/pause", (TradingPauseRequest? request, ITradingControlService tradingControl) =>
    {
        var status = tradingControl.Pause(request?.Reason, request?.PausedBy);
        var response = new TradingPauseResponse(
            Message: status.IsTradingEnabled ? "Trading was already paused" : "Trading paused successfully",
            IsTradingEnabled: status.IsTradingEnabled,
            Status: status.IsTradingEnabled ? "Active" : "Paused",
            PausedAt: status.PausedAt,
            PausedBy: status.PausedBy,
            PauseReason: status.PauseReason
        );
        return Results.Ok(response);
    })
    .WithName("PauseTrading")
    .WithTags("Trading Control")
    .WithSummary("Pause trading")
    .WithDescription("Pauses all trading activity. Optionally accepts a reason and identifier for who paused trading. Returns success even if trading was already paused.")
    .Accepts<TradingPauseRequest>("application/json")
    .Produces<TradingPauseResponse>(StatusCodes.Status200OK)
    .WithOpenApi();

    app.MapPost("/trading/resume", (TradingResumeRequest? request, ITradingControlService tradingControl) =>
    {
        var status = tradingControl.Resume(request?.ResumedBy);
        var response = new TradingResumeResponse(
            Message: status.IsTradingEnabled ? "Trading resumed successfully" : "Trading was already active",
            IsTradingEnabled: status.IsTradingEnabled,
            Status: status.IsTradingEnabled ? "Active" : "Paused"
        );
        return Results.Ok(response);
    })
    .WithName("ResumeTrading")
    .WithTags("Trading Control")
    .WithSummary("Resume trading")
    .WithDescription("Resumes trading activity if it was previously paused. Optionally accepts an identifier for who resumed trading. Returns success even if trading was already active.")
    .Accepts<TradingResumeRequest>("application/json")
    .Produces<TradingResumeResponse>(StatusCodes.Status200OK)
    .WithOpenApi();

    Log.Information("QuantFlow.Orchestrator starting up");
    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Application terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}

/// <summary>Request to pause trading</summary>
/// <param name="Reason">Optional reason for pausing trading</param>
/// <param name="PausedBy">Optional identifier for who paused trading</param>
public record TradingPauseRequest(string? Reason, string? PausedBy);

/// <summary>Request to resume trading</summary>
/// <param name="ResumedBy">Optional identifier for who resumed trading</param>
public record TradingResumeRequest(string? ResumedBy);
