namespace AxiApi.Observability;

public sealed class ObservabilityOptions
{
    public bool Enabled { get; set; }
    public string Endpoint { get; set; } = "http://localhost:4318";
    public double TraceSampleRatio { get; set; } = 1;
    public int MetricExportIntervalMilliseconds { get; set; } = 15000;
    public int ExportTimeoutMilliseconds { get; set; } = 2000;
    public int BatchDelayMilliseconds { get; set; } = 1000;
    public int QueueSize { get; set; } = 2048;
    public int BatchSize { get; set; } = 256;
    public string ApplicationLogLevel { get; set; } = "Information";
    public string FrameworkLogLevel { get; set; } = "Warning";

    internal void Validate()
    {
        if (!Uri.TryCreate(Endpoint, UriKind.Absolute, out var endpoint) ||
            endpoint.Scheme is not ("http" or "https") || endpoint.UserInfo.Length != 0 ||
            endpoint.Query.Length != 0 || endpoint.Fragment.Length != 0)
            throw new InvalidOperationException("Observability:Endpoint must be an HTTP(S) Collector base URL without credentials, query, or fragment.");
        if (TraceSampleRatio is < 0 or > 1 || double.IsNaN(TraceSampleRatio) ||
            MetricExportIntervalMilliseconds <= 0 || ExportTimeoutMilliseconds <= 0 ||
            BatchDelayMilliseconds <= 0 || QueueSize <= 0 || BatchSize <= 0 || BatchSize > QueueSize)
            throw new InvalidOperationException("Observability collection settings must be positive; sampling must be between 0 and 1 and batch size cannot exceed queue size.");
    }
}
