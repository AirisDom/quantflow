using System.Runtime.InteropServices;
using QuantFlow.Orchestrator.Clients;

namespace QuantFlow.Orchestrator.Shutdown;

public class GracefulShutdownHostedService : IHostedService
{
    private readonly IHostApplicationLifetime _applicationLifetime;
    private readonly IGracefulShutdownService _shutdownService;
    private readonly ISignalServiceClient _signalClient;
    private readonly IExecutionServiceClient _executionClient;
    private readonly ILogger<GracefulShutdownHostedService> _logger;
    private readonly TimeSpan _drainTimeout = TimeSpan.FromSeconds(30);

    public GracefulShutdownHostedService(
        IHostApplicationLifetime applicationLifetime,
        IGracefulShutdownService shutdownService,
        ISignalServiceClient signalClient,
        IExecutionServiceClient executionClient,
        ILogger<GracefulShutdownHostedService> logger)
    {
        _applicationLifetime = applicationLifetime;
        _shutdownService = shutdownService;
        _signalClient = signalClient;
        _executionClient = executionClient;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _applicationLifetime.ApplicationStopping.Register(OnStopping);
        _applicationLifetime.ApplicationStopped.Register(OnStopped);

        RegisterSignalHandlers();

        _logger.LogInformation("GracefulShutdownHostedService started, signal handlers registered");
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    private void RegisterSignalHandlers()
    {
        PosixSignalRegistration.Create(PosixSignal.SIGTERM, OnSignalReceived);
        PosixSignalRegistration.Create(PosixSignal.SIGINT, OnSignalReceived);

        _logger.LogDebug("Registered SIGTERM and SIGINT handlers");
    }

    private void OnSignalReceived(PosixSignalContext context)
    {
        _logger.LogInformation("Received signal {Signal}, initiating graceful shutdown", context.Signal);
        context.Cancel = true;
        _applicationLifetime.StopApplication();
    }

    private void OnStopping()
    {
        _logger.LogInformation("Application stopping, beginning graceful shutdown sequence");
        _shutdownService.BeginShutdown();

        try
        {
            _shutdownService.WaitForDrainAsync(_drainTimeout, CancellationToken.None)
                .GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during request drain");
        }

        CloseGrpcChannels();
    }

    private void OnStopped()
    {
        _logger.LogInformation("Application stopped, graceful shutdown complete");
    }

    private void CloseGrpcChannels()
    {
        _logger.LogInformation("Closing gRPC channels");

        try
        {
            _signalClient.Dispose();
            _logger.LogDebug("SignalServiceClient disposed");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error disposing SignalServiceClient");
        }

        try
        {
            _executionClient.Dispose();
            _logger.LogDebug("ExecutionServiceClient disposed");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error disposing ExecutionServiceClient");
        }

        _logger.LogInformation("gRPC channels closed");
    }
}
