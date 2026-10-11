using System.Diagnostics;
using Microsoft.AspNetCore.Routing;

namespace AxiApi.Observability;

public sealed class RequestTelemetryMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, ILogger<RequestTelemetryMiddleware> logger)
    {
        var route = (context.GetEndpoint() as RouteEndpoint)?.RoutePattern.RawText ?? "unmatched";
        var method = context.Request.Method switch
        {
            "GET" or "POST" or "PUT" or "PATCH" or "DELETE" or "HEAD" or "OPTIONS" => context.Request.Method,
            _ => "OTHER"
        };
        using var activity = AxiTelemetry.Source.StartActivity(method + " " + route, ActivityKind.Server);
        // Keep distributed trace IDs, but do not export caller-controlled vendor state.
        if (activity is not null) activity.TraceStateString = string.Empty;
        activity?.SetTag("http.request.method", method);
        activity?.SetTag("http.route", route);
        var started = Stopwatch.GetTimestamp();
        var status = 500;
        try
        {
            await next(context);
            status = context.Response.StatusCode;
        }
        finally
        {
            activity?.SetTag("http.response.status_code", status);
            if (status >= 500) activity?.SetStatus(ActivityStatusCode.Error);
            AxiTelemetry.Request(method, route, status, Stopwatch.GetElapsedTime(started).TotalSeconds);
            logger.LogInformation("HTTP request completed");
        }
    }
}
