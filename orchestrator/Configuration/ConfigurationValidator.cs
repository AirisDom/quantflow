using System.ComponentModel.DataAnnotations;

namespace QuantFlow.Orchestrator.Configuration;

public static class ConfigurationValidator
{
    public static void ValidateAndThrow<T>(T settings, string sectionName) where T : class
    {
        var results = new List<ValidationResult>();
        var context = new ValidationContext(settings);

        if (!Validator.TryValidateObject(settings, context, results, validateAllProperties: true))
        {
            var errors = string.Join(Environment.NewLine, results.Select(r => $"  - {r.ErrorMessage}"));
            throw new ConfigurationValidationException(
                $"Configuration validation failed for '{sectionName}':{Environment.NewLine}{errors}");
        }

        if (settings is IValidatableObject validatable)
        {
            var customResults = validatable.Validate(context);
            if (customResults.Any())
            {
                var errors = string.Join(Environment.NewLine, customResults.Select(r => $"  - {r.ErrorMessage}"));
                throw new ConfigurationValidationException(
                    $"Configuration validation failed for '{sectionName}':{Environment.NewLine}{errors}");
            }
        }
    }

    public static void ValidateConnectionString(string? connectionString, string name)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new ConfigurationValidationException(
                $"Connection string '{name}' is required but was not provided.");
        }

        if (connectionString.Contains("changeme", StringComparison.OrdinalIgnoreCase) ||
            connectionString.Contains("your_password", StringComparison.OrdinalIgnoreCase))
        {
            throw new ConfigurationValidationException(
                $"Connection string '{name}' appears to contain placeholder values. Please update with actual credentials.");
        }
    }
}

public class ConfigurationValidationException : Exception
{
    public ConfigurationValidationException(string message) : base(message) { }
}
