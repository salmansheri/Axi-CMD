using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Logs;
using Serilog.Core;
using Serilog.Events;

namespace AxiApi.Observability;

// Export an allowlisted summary, never the original rendered message, properties or exception.
public sealed class SafeOtelLogSink : ILogEventSink, IDisposable
{
    private readonly ILoggerFactory factory;
    private readonly Microsoft.Extensions.Logging.ILogger logger;
    private readonly LogLevel applicationLevel;
    private readonly LogLevel frameworkLevel;

    public SafeOtelLogSink(ObservabilityOptions options)
    {
        if (!Enum.TryParse(options.ApplicationLogLevel, true, out applicationLevel) ||
            !Enum.TryParse(options.FrameworkLogLevel, true, out frameworkLevel))
            throw new InvalidOperationException("Observability log levels must be valid .NET LogLevel values.");
        factory = LoggerFactory.Create(logging => logging
            .SetMinimumLevel(LogLevel.Trace)
            .AddOpenTelemetry(otel =>
            {
                otel.SetResourceBuilder(ObservabilityRegistration.Resource());
                otel.IncludeScopes = false;
                otel.IncludeFormattedMessage = true;
                otel.AddOtlpExporter((export, processor) =>
                {
                    ObservabilityRegistration.ConfigureExporter(export, options, "logs");
                    processor.ExportProcessorType = ExportProcessorType.Batch;
                    processor.BatchExportProcessorOptions = new BatchExportLogRecordProcessorOptions
                    {
                        MaxQueueSize = options.QueueSize,
                        MaxExportBatchSize = options.BatchSize,
                        ScheduledDelayMilliseconds = options.BatchDelayMilliseconds,
                        ExporterTimeoutMilliseconds = options.ExportTimeoutMilliseconds
                    };
                });
            }));
        logger = factory.CreateLogger("AxiApi");
    }

    public void Emit(LogEvent logEvent)
    {
        var level = logEvent.Level switch
        {
            LogEventLevel.Verbose => LogLevel.Trace,
            LogEventLevel.Debug => LogLevel.Debug,
            LogEventLevel.Information => LogLevel.Information,
            LogEventLevel.Warning => LogLevel.Warning,
            LogEventLevel.Error => LogLevel.Error,
            _ => LogLevel.Critical
        };
        var framework = logEvent.Properties.TryGetValue("SourceContext", out var source) &&
            source is ScalarValue { Value: string name } &&
            (name.StartsWith("Microsoft.", StringComparison.Ordinal) || name.StartsWith("System.", StringComparison.Ordinal));
        if (level < (framework ? frameworkLevel : applicationLevel)) return;

        var summary = logEvent.MessageTemplate.Text switch
        {
            "HTTP request completed" => "HTTP request completed",
            "Database retry scheduled" => "Database retry scheduled",
            "Dependency operation completed: {Dependency} {Operation} {Outcome}" => "Dependency operation completed",
            "Cache outcome recorded: {Operation} {Outcome}" => "Cache outcome recorded",
            "Unhandled exception occured" => "Request failed",
            "Bootstrap: CRITICAL FAILURE. Grammar could not be initialized." => "Grammar initialization failed",
            _ when logEvent.Exception is not null => "Application operation raised an exception",
            _ => framework ? "Framework event" : "Application event"
        };
        var fields = new List<KeyValuePair<string, object?>> { new("{OriginalFormat}", summary) };
        if (logEvent.MessageTemplate.Text == "Dependency operation completed: {Dependency} {Operation} {Outcome}")
            AddSafe("Dependency", "axi.dependency", "database", "cache");
        if (logEvent.MessageTemplate.Text is "Dependency operation completed: {Dependency} {Operation} {Outcome}" or
            "Cache outcome recorded: {Operation} {Outcome}")
        {
            AddSafe("Operation", "axi.operation", "connect", "query", "write", "read", "invalidate", "grammar", "command-config", "favourites");
            AddSafe("Outcome", "axi.outcome", "success", "error", "hit", "miss", "force_refresh", "successful_fallback", "write_failure");
        }
        logger.Log(level, new EventId(0), fields, null, (_, _) => summary);

        void AddSafe(string property, string attribute, params string[] allowed)
        {
            if (logEvent.Properties.TryGetValue(property, out var value) &&
                value is ScalarValue { Value: string text } && allowed.Contains(text, StringComparer.Ordinal))
                fields.Add(new(attribute, text));
        }
    }

    public void Dispose() => factory.Dispose();
}
