using System.ComponentModel.DataAnnotations;

namespace QuantFlow.Orchestrator.Configuration;

public class QuantFlowSettings
{
    public SignalServiceSettings SignalService { get; set; } = new();
    public ExecutionServiceSettings ExecutionService { get; set; } = new();
    public PriceTickerSettings PriceTicker { get; set; } = new();
    public PortfolioSettings Portfolio { get; set; } = new();
    public RiskSettings Risk { get; set; } = new();
}

public class SignalServiceSettings : IValidatableObject
{
    [Required(ErrorMessage = "SignalService Address is required")]
    public string Address { get; set; } = string.Empty;

    [Range(1, 10, ErrorMessage = "MaxRetryAttempts must be between 1 and 10")]
    public int MaxRetryAttempts { get; set; } = 5;

    [Range(10, 10000, ErrorMessage = "InitialBackoffMs must be between 10 and 10000")]
    public int InitialBackoffMs { get; set; } = 100;

    [Range(100, 60000, ErrorMessage = "MaxBackoffMs must be between 100 and 60000")]
    public int MaxBackoffMs { get; set; } = 5000;

    [Range(1.0, 5.0, ErrorMessage = "BackoffMultiplier must be between 1.0 and 5.0")]
    public double BackoffMultiplier { get; set; } = 2.0;

    [Range(1000, 120000, ErrorMessage = "DeadlineMs must be between 1000 and 120000")]
    public int DeadlineMs { get; set; } = 30000;

    [Range(10, 300, ErrorMessage = "IdleTimeoutSeconds must be between 10 and 300")]
    public int IdleTimeoutSeconds { get; set; } = 60;

    [Range(5, 120, ErrorMessage = "KeepAlivePingDelaySeconds must be between 5 and 120")]
    public int KeepAlivePingDelaySeconds { get; set; } = 30;

    [Range(5, 60, ErrorMessage = "KeepAlivePingTimeoutSeconds must be between 5 and 60")]
    public int KeepAlivePingTimeoutSeconds { get; set; } = 10;

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (!string.IsNullOrEmpty(Address) && !Uri.TryCreate(Address, UriKind.Absolute, out _))
        {
            yield return new ValidationResult("SignalService Address must be a valid URI", [nameof(Address)]);
        }

        if (InitialBackoffMs > MaxBackoffMs)
        {
            yield return new ValidationResult("InitialBackoffMs cannot exceed MaxBackoffMs", [nameof(InitialBackoffMs)]);
        }
    }
}

public class ExecutionServiceSettings : IValidatableObject
{
    [Required(ErrorMessage = "ExecutionService Address is required")]
    public string Address { get; set; } = string.Empty;

    [Range(1, 10, ErrorMessage = "MaxRetryAttempts must be between 1 and 10")]
    public int MaxRetryAttempts { get; set; } = 3;

    [Range(10, 5000, ErrorMessage = "InitialBackoffMs must be between 10 and 5000")]
    public int InitialBackoffMs { get; set; } = 50;

    [Range(100, 30000, ErrorMessage = "MaxBackoffMs must be between 100 and 30000")]
    public int MaxBackoffMs { get; set; } = 1000;

    [Range(1.0, 5.0, ErrorMessage = "BackoffMultiplier must be between 1.0 and 5.0")]
    public double BackoffMultiplier { get; set; } = 1.5;

    [Range(500, 60000, ErrorMessage = "DeadlineMs must be between 500 and 60000")]
    public int DeadlineMs { get; set; } = 5000;

    [Range(10, 300, ErrorMessage = "IdleTimeoutSeconds must be between 10 and 300")]
    public int IdleTimeoutSeconds { get; set; } = 60;

    [Range(5, 120, ErrorMessage = "KeepAlivePingDelaySeconds must be between 5 and 120")]
    public int KeepAlivePingDelaySeconds { get; set; } = 30;

    [Range(5, 60, ErrorMessage = "KeepAlivePingTimeoutSeconds must be between 5 and 60")]
    public int KeepAlivePingTimeoutSeconds { get; set; } = 10;

    [Range(1, 100, ErrorMessage = "CircuitBreakerFailureThreshold must be between 1 and 100")]
    public int CircuitBreakerFailureThreshold { get; set; } = 5;

    [Range(1000, 300000, ErrorMessage = "CircuitBreakerResetTimeMs must be between 1000 and 300000")]
    public int CircuitBreakerResetTimeMs { get; set; } = 30000;

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (!string.IsNullOrEmpty(Address) && !Uri.TryCreate(Address, UriKind.Absolute, out _))
        {
            yield return new ValidationResult("ExecutionService Address must be a valid URI", [nameof(Address)]);
        }

        if (InitialBackoffMs > MaxBackoffMs)
        {
            yield return new ValidationResult("InitialBackoffMs cannot exceed MaxBackoffMs", [nameof(InitialBackoffMs)]);
        }
    }
}

public class PriceTickerSettings : IValidatableObject
{
    public string[] Assets { get; set; } = ["BTC", "ETH", "SPY"];

    [Range(100, 60000, ErrorMessage = "TickIntervalMs must be between 100 and 60000")]
    public int TickIntervalMs { get; set; } = 1000;

    public Dictionary<string, decimal> InitialPrices { get; set; } = new()
    {
        ["BTC"] = 67500.00m,
        ["ETH"] = 3450.00m,
        ["SPY"] = 542.50m
    };

    public Dictionary<string, decimal> Volatility { get; set; } = new()
    {
        ["BTC"] = 0.02m,
        ["ETH"] = 0.025m,
        ["SPY"] = 0.005m
    };

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Assets == null || Assets.Length == 0)
        {
            yield return new ValidationResult("At least one asset must be configured", [nameof(Assets)]);
        }

        foreach (var kvp in Volatility)
        {
            if (kvp.Value < 0 || kvp.Value > 1)
            {
                yield return new ValidationResult($"Volatility for {kvp.Key} must be between 0 and 1", [nameof(Volatility)]);
            }
        }

        foreach (var kvp in InitialPrices)
        {
            if (kvp.Value <= 0)
            {
                yield return new ValidationResult($"Initial price for {kvp.Key} must be positive", [nameof(InitialPrices)]);
            }
        }
    }
}

public class PortfolioSettings
{
    [Range(0, double.MaxValue, ErrorMessage = "InitialCash must be non-negative")]
    public decimal InitialCash { get; set; } = 100_000m;
}

public class RiskSettings : IValidatableObject
{
    [Range(0.001, 1.0, ErrorMessage = "MaxDrawdownPercent must be between 0.1% and 100%")]
    public decimal MaxDrawdownPercent { get; set; } = 0.05m;

    [Range(0.001, 1.0, ErrorMessage = "MaxPositionSizePercent must be between 0.1% and 100%")]
    public decimal MaxPositionSizePercent { get; set; } = 0.10m;

    [Range(0.001, 1.0, ErrorMessage = "MaxExposurePercent must be between 0.1% and 100%")]
    public decimal MaxExposurePercent { get; set; } = 0.80m;

    [Range(0, double.MaxValue, ErrorMessage = "MinOrderValue must be non-negative")]
    public decimal MinOrderValue { get; set; } = 10m;

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (MaxPositionSizePercent > MaxExposurePercent)
        {
            yield return new ValidationResult(
                "MaxPositionSizePercent cannot exceed MaxExposurePercent",
                [nameof(MaxPositionSizePercent), nameof(MaxExposurePercent)]);
        }
    }
}
