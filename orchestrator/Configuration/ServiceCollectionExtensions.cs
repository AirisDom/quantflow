using Microsoft.Extensions.Options;

namespace QuantFlow.Orchestrator.Configuration;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddQuantFlowConfiguration(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<QuantFlowSettings>()
            .Bind(configuration)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<SignalServiceSettings>()
            .Bind(configuration.GetSection("SignalService"))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<ExecutionServiceSettings>()
            .Bind(configuration.GetSection("ExecutionService"))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<PriceTickerSettings>()
            .Bind(configuration.GetSection("PriceTicker"))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<PortfolioSettings>()
            .Bind(configuration.GetSection("Portfolio"))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<RiskSettings>()
            .Bind(configuration.GetSection("Risk"))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        return services;
    }

    public static void ValidateRequiredConfiguration(this IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("DefaultConnection");
        ConfigurationValidator.ValidateConnectionString(connectionString, "DefaultConnection");

        var signalServiceSettings = new SignalServiceSettings();
        configuration.GetSection("SignalService").Bind(signalServiceSettings);
        ConfigurationValidator.ValidateAndThrow(signalServiceSettings, "SignalService");

        var executionServiceSettings = new ExecutionServiceSettings();
        configuration.GetSection("ExecutionService").Bind(executionServiceSettings);
        ConfigurationValidator.ValidateAndThrow(executionServiceSettings, "ExecutionService");

        var priceTickerSettings = new PriceTickerSettings();
        configuration.GetSection("PriceTicker").Bind(priceTickerSettings);
        ConfigurationValidator.ValidateAndThrow(priceTickerSettings, "PriceTicker");

        var portfolioSettings = new PortfolioSettings();
        configuration.GetSection("Portfolio").Bind(portfolioSettings);
        ConfigurationValidator.ValidateAndThrow(portfolioSettings, "Portfolio");

        var riskSettings = new RiskSettings();
        configuration.GetSection("Risk").Bind(riskSettings);
        ConfigurationValidator.ValidateAndThrow(riskSettings, "Risk");
    }
}
