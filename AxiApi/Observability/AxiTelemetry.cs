using System.Diagnostics;
using System.Diagnostics.Metrics;
using ARMCommon.Model;
using Serilog;

namespace AxiApi.Observability;

// Only callers in AxiApi supply operation names. Never pass application values as tags.
internal static class AxiTelemetry
{
    public const string Name = "AxiApi.Observability";
    public static readonly ActivitySource Source = new(Name);
    private static readonly Meter Meter = new(Name);
    private static readonly Histogram<double> HttpDuration = Meter.CreateHistogram<double>("axi.http.request.duration", "s");
    private static readonly Counter<long> HttpRequests = Meter.CreateCounter<long>("axi.http.requests");
    private static readonly Histogram<double> DependencyDuration = Meter.CreateHistogram<double>("axi.dependency.duration", "s");
    private static readonly Counter<long> DependencyCalls = Meter.CreateCounter<long>("axi.dependency.calls");
    private static readonly Counter<long> TerminalFailures = Meter.CreateCounter<long>("axi.dependency.failures");
    private static readonly Counter<long> Retries = Meter.CreateCounter<long>("axi.database.retries");
    private static readonly Counter<long> CacheOutcomes = Meter.CreateCounter<long>("axi.cache.outcomes");

    public static void Request(string method, string route, int status, double seconds)
    {
        var tags = new TagList { { "http.request.method", method }, { "http.route", route }, { "http.response.status_code", status } };
        HttpDuration.Record(seconds, tags);
        HttpRequests.Add(1, tags);
    }

    public static async Task<T> DependencyAsync<T>(string dependency, string operation, Func<Task<T>> action)
    {
        using var activity = Source.StartActivity(dependency + "." + operation, ActivityKind.Client);
        activity?.SetTag("axi.dependency", dependency);
        activity?.SetTag("axi.operation", operation);
        var started = Stopwatch.GetTimestamp();
        var outcome = "error";
        try
        {
            var result = await action();
            outcome = result switch
            {
                bool value when !value && operation is "connect" or "write" => "error",
                SQLResult sql when !string.IsNullOrEmpty(sql.error) => "error",
                NonQueryResult sql when !string.IsNullOrEmpty(sql.error) => "error",
                _ => "success"
            };
            return result;
        }
        finally
        {
            activity?.SetTag("axi.outcome", outcome);
            if (outcome == "error") activity?.SetStatus(ActivityStatusCode.Error);
            var tags = new TagList { { "axi.dependency", dependency }, { "axi.operation", operation }, { "axi.outcome", outcome } };
            DependencyDuration.Record(Stopwatch.GetElapsedTime(started).TotalSeconds, tags);
            DependencyCalls.Add(1, tags);
            Log.Information("Dependency operation completed: {Dependency} {Operation} {Outcome}", dependency, operation, outcome);
        }
    }

    public static void Failure(string dependency, string operation)
    {
        TerminalFailures.Add(1, new TagList { { "axi.dependency", dependency }, { "axi.operation", operation } });
        for (var activity = Activity.Current; activity?.Source.Name == Name; activity = activity.Parent)
            activity.SetStatus(ActivityStatusCode.Error);
        Activity.Current?.AddEvent(new ActivityEvent("dependency.terminal_failure", tags:
            new ActivityTagsCollection { { "axi.dependency", dependency }, { "axi.operation", operation } }));
    }

    public static DatabaseOperation BeginDatabase(string operation) => new(operation);

    internal sealed class DatabaseOperation : IDisposable
    {
        private readonly string operation;
        private readonly Activity? activity;
        private bool succeeded;

        public DatabaseOperation(string operation)
        {
            this.operation = operation;
            activity = Source.StartActivity("database." + operation);
            activity?.SetTag("axi.dependency", "database");
            activity?.SetTag("axi.operation", operation);
        }

        public void Complete(bool success = true) => succeeded = success;

        public void Dispose()
        {
            activity?.SetTag("axi.outcome", succeeded ? "success" : "error");
            if (!succeeded) Failure("database", operation);
            activity?.Dispose();
        }
    }

    public static void Retry(string operation)
    {
        Retries.Add(1, new KeyValuePair<string, object?>("axi.operation", operation));
        Activity.Current?.AddEvent(new ActivityEvent("database.retry"));
        Log.Warning("Database retry scheduled");
    }

    public static void Cache(string operation, string outcome)
    {
        CacheOutcomes.Add(1, new TagList { { "axi.operation", operation }, { "axi.outcome", outcome } });
        Activity.Current?.AddEvent(new ActivityEvent("cache." + outcome));
        Log.Information("Cache outcome recorded: {Operation} {Outcome}", operation, outcome);
    }
}
