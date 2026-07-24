using System.Diagnostics.CodeAnalysis;

[assembly: SuppressMessage("Performance", "CA1848:Use the LoggerMessage delegates",
    Justification = "Project uses Serilog with structured logging which handles this efficiently")]
[assembly: SuppressMessage("Performance", "CA1873:Logging arguments may be evaluated unnecessarily",
    Justification = "Project uses Serilog with structured logging which handles this efficiently")]
