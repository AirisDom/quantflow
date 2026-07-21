using Grpc.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using QuantFlow.Protos;

namespace QuantFlow.Orchestrator.IntegrationTests.TestServers;

public class GrpcTestServerFactory : IDisposable
{
    private readonly List<WebApplication> _runningApps = new();
    private static int _nextPort = 15000 + Random.Shared.Next(1000, 5000);
    private static readonly object PortLock = new();

    public (WebApplication app, string address) CreateSignalServiceServer(MockSignalService mockService)
    {
        var port = GetNextPort();
        var address = $"http://localhost:{port}";

        var builder = WebApplication.CreateBuilder();
        builder.Services.AddGrpc();
        builder.Services.AddSingleton(mockService);
        builder.WebHost.ConfigureKestrel(options =>
        {
            options.ListenLocalhost(port, listenOptions =>
            {
                listenOptions.Protocols = HttpProtocols.Http2;
            });
        });

        var app = builder.Build();
        app.MapGrpcService<TestSignalServiceImpl>();

        lock (_runningApps)
        {
            _runningApps.Add(app);
        }

        return (app, address);
    }

    public (WebApplication app, string address) CreateExecutionServiceServer(MockExecutionService mockService)
    {
        var port = GetNextPort();
        var address = $"http://localhost:{port}";

        var builder = WebApplication.CreateBuilder();
        builder.Services.AddGrpc();
        builder.Services.AddSingleton(mockService);
        builder.WebHost.ConfigureKestrel(options =>
        {
            options.ListenLocalhost(port, listenOptions =>
            {
                listenOptions.Protocols = HttpProtocols.Http2;
            });
        });

        var app = builder.Build();
        app.MapGrpcService<TestExecutionServiceImpl>();

        lock (_runningApps)
        {
            _runningApps.Add(app);
        }

        return (app, address);
    }

    private static int GetNextPort()
    {
        lock (PortLock)
        {
            return Interlocked.Increment(ref _nextPort);
        }
    }

    public void Dispose()
    {
        foreach (var app in _runningApps)
        {
            try
            {
                app.StopAsync().GetAwaiter().GetResult();
                app.DisposeAsync().GetAwaiter().GetResult();
            }
            catch
            {
            }
        }
        _runningApps.Clear();
    }
}

public class TestSignalServiceImpl : SignalService.SignalServiceBase
{
    private readonly MockSignalService _mock;

    public TestSignalServiceImpl(MockSignalService mock)
    {
        _mock = mock;
    }

    public override Task<TradeSignal> GetSignal(IAsyncStreamReader<PriceTick> requestStream, ServerCallContext context)
    {
        return _mock.GetSignal(requestStream, context);
    }
}

public class TestExecutionServiceImpl : ExecutionService.ExecutionServiceBase
{
    private readonly MockExecutionService _mock;

    public TestExecutionServiceImpl(MockExecutionService mock)
    {
        _mock = mock;
    }

    public override Task<ExecutionReceipt> ExecuteOrder(OrderRequest request, ServerCallContext context)
    {
        return _mock.ExecuteOrder(request, context);
    }
}
