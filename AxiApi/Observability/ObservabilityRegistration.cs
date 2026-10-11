using OpenTelemetry;
using OpenTelemetry.Exporter;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace AxiApi.Observability;

public static class ObservabilityRegistration
{
    public static ObservabilityOptions AddAxiObservability(this IServiceCollection services, IConfiguration configuration)
    {
        var options = configuration.GetSection("Observability").Get<ObservabilityOptions>() ?? new();
        services.AddSingleton(options);
        if (!options.Enabled) return options;
        options.Validate();
        services.AddSingleton<SafeOtelLogSink>();
        services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.Clear().AddService("AxiApi", autoGenerateServiceInstanceId: false))
            .WithTracing(tracing => tracing
                .AddSource(AxiTelemetry.Name)
                .SetSampler(new TraceIdRatioBasedSampler(options.TraceSampleRatio))
                .AddOtlpExporter(export =>
                {
                    ConfigureExporter(export, options, "traces");
                    export.BatchExportProcessorOptions = new BatchExportActivityProcessorOptions
                    {
                        MaxQueueSize = options.QueueSize,
                        MaxExportBatchSize = options.BatchSize,
                        ScheduledDelayMilliseconds = options.BatchDelayMilliseconds,
                        ExporterTimeoutMilliseconds = options.ExportTimeoutMilliseconds
                    };
                }))
            .WithMetrics(metrics => metrics
                .AddMeter(AxiTelemetry.Name)
                .AddView("*.duration", new ExplicitBucketHistogramConfiguration
                {
                    Boundaries = [0.005, 0.01, 0.025, 0.05, 0.1, 0.25, 0.5, 1, 2.5, 5, 10]
                })
                .AddOtlpExporter((export, reader) =>
                {
                    ConfigureExporter(export, options, "metrics");
                    reader.PeriodicExportingMetricReaderOptions.ExportIntervalMilliseconds = options.MetricExportIntervalMilliseconds;
                    reader.PeriodicExportingMetricReaderOptions.ExportTimeoutMilliseconds = options.ExportTimeoutMilliseconds;
                }));
        return options;
    }

    internal static ResourceBuilder Resource() => ResourceBuilder.CreateEmpty()
        .AddService("AxiApi", autoGenerateServiceInstanceId: false);

    internal static void ConfigureExporter(OtlpExporterOptions export, ObservabilityOptions options, string signal)
    {
        export.Endpoint = new Uri(options.Endpoint.TrimEnd('/') + "/v1/" + signal);
        export.Protocol = OtlpExportProtocol.HttpProtobuf;
        export.TimeoutMilliseconds = options.ExportTimeoutMilliseconds;
    }
}
