using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Data;
using System.Net.Http.Json;
using ARMCommon.Interface;
using ARMCommon.Model;
using AxExtend.Interface;
using AxiApi.Controllers;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Moq;
using StackExchange.Redis;
using Xunit;
using HttpServer = Microsoft.AspNetCore.Hosting.Server.IServer;

namespace AxiApi.Tests;

public class ObservabilityTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Grammar_cache_and_database_payloads_are_not_exported(bool hit)
    {
        await using var collector = await TestCollector.StartAsync();
        using var app = CreateApp(collector.Endpoint, configure: (ax, db, redis) =>
        {
            ax.Setup(x => x.OpenRedisConnectionAsync(It.IsAny<string>())).ReturnsAsync(hit);
            redis.Setup(x => x.StringGetAsync(It.IsAny<string>())).ReturnsAsync(
                "{\"Commands\":[{\"CmdToken\":1,\"CommandGroup\":\"PRIVATE_GROUP\",\"Command\":\"PRIVATE_COMMAND\"}],\"Prompts\":[]}");
            var table = new DataTable();
            foreach (var column in new[] { "cmdtoken", "command_group", "command", "wordpos", "prompt",
                "promptsource", "promptparams", "promptvalues", "extraparams" }) table.Columns.Add(column);
            table.Rows.Add("1", "PRIVATE_GROUP", "PRIVATE_COMMAND", DBNull.Value, DBNull.Value,
                DBNull.Value, DBNull.Value, DBNull.Value, DBNull.Value);
            db.Setup(x => x.ExecuteSQLAsync(It.IsAny<string>(), It.IsAny<string[]>(),
                It.IsAny<DbType[]>(), It.IsAny<object[]>())).ReturnsAsync(new SQLResult { data = table });
        });
        using var client = app.CreateClient(new() { BaseAddress = new Uri("https://localhost") });
        using var response = await client.GetAsync("/api/v1/Axi/axi_get?view=Metadata&forceRefresh=false&appname=PRIVATE_TENANT");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("PRIVATE_COMMAND", await response.Content.ReadAsStringAsync());
        var outcome = hit ? "hit" : "successful_fallback";
        await collector.WaitForAsync("/v1/metrics", outcome);
        await collector.WaitForAsync("/v1/logs", "HTTP request completed");
        await collector.WaitForAsync("/v1/traces", "axi_get");
        Assert.Equal(1, collector.Counter("axi.cache.outcomes", ("axi.operation", "grammar"), ("axi.outcome", outcome)));
        Assert.DoesNotContain("PRIVATE_", collector.Text);
        Assert.Contains(collector.Logs, record => record.Messages(6).Any(pair =>
            pair.String(1) == "axi.outcome" && pair.Messages(2).Single().String(1) == outcome));
    }

    [Fact]
    public async Task Favourites_cache_unavailability_remains_a_terminal_failure()
    {
        await using var collector = await TestCollector.StartAsync();
        using var app = CreateApp(collector.Endpoint);
        using var client = app.CreateClient(new() { BaseAddress = new Uri("https://localhost") });
        using var response = await client.GetAsync("/api/v1/Axi/user-favourites?appname=PRIVATE_TENANT&username=PRIVATE_USER");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await collector.WaitForAsync("/v1/metrics", "axi.dependency.failures");
        await collector.WaitForAsync("/v1/logs", "Request failed");
        await collector.WaitForAsync("/v1/traces", "user-favourites");
        Assert.Equal(1, collector.Counter("axi.dependency.failures", ("axi.dependency", "cache")));
        Assert.DoesNotContain("PRIVATE_", collector.Text);
    }

    [Fact]
    public async Task Sql_error_values_and_nested_exception_messages_are_not_exported()
    {
        await using var collector = await TestCollector.StartAsync();
        using var app = CreateApp(collector.Endpoint, configure: (_, db, _) =>
            db.Setup(x => x.ExecuteSQLAsync(It.IsAny<string>(), It.IsAny<string[]>(),
                It.IsAny<DbType[]>(), It.IsAny<object[]>())).ReturnsAsync(new SQLResult
                { error = "SELECT PRIVATE_SQL_VALUE; password=PRIVATE_CREDENTIAL" }));
        using var client = app.CreateClient(new() { BaseAddress = new Uri("https://localhost") });
        using var response = await client.GetAsync("/api/v1/Axi/command-config?appname=PRIVATE_TENANT&username=PRIVATE_USER");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await collector.WaitForAsync("/v1/metrics", "axi.dependency.failures");
        await collector.WaitForAsync("/v1/logs", "Request failed");
        await collector.WaitForAsync("/v1/traces", "command-config");
        Assert.Equal(1, collector.Counter("axi.dependency.failures"));
        Assert.DoesNotContain("PRIVATE_", collector.Text);
    }

    [Fact]
    public async Task Collector_outage_does_not_fail_requests_or_retain_unbounded_request_data()
    {
        await using var collector = await TestCollector.StartAsync();
        collector.BlockExports = true;
        using var app = CreateApp(collector.Endpoint);
        using var client = app.CreateClient(new() { BaseAddress = new Uri("https://localhost") });
        async Task Traffic(int count)
        {
            for (var i = 0; i < count; i++)
            {
                using var response = await client.GetAsync(
                    "/api/v1/Axi/command-config?appname=PRIVATE_TENANT_" + i + "&username=PRIVATE_USER_" + i);
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            }
        }
        await Traffic(150); // Warm up application caches and fill the configured 32-record export queues.
        var before = GC.GetTotalMemory(forceFullCollection: true);
        await Traffic(500);
        var after = GC.GetTotalMemory(forceFullCollection: true);
        // Detect retention of per-request data, allowing runtime bookkeeping rather than asserting exact allocation.
        Assert.True(after - before < 16 * 1024 * 1024, $"Retained memory grew by {after - before} bytes during outage.");
        collector.BlockExports = false;
    }

    [Fact]
    public async Task Unreachable_collector_does_not_fail_api_requests()
    {
        await using var collector = await TestCollector.StartAsync();
        var endpoint = collector.Endpoint;
        await collector.DisposeAsync();
        using var app = CreateApp(endpoint);
        using var client = app.CreateClient(new() { BaseAddress = new Uri("https://localhost") });
        using var response = await client.GetAsync("/api/v1/Axi/command-config?appname=PRIVATE_TENANT&username=PRIVATE_USER");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Database_retries_distinguish_recovery_from_exhaustion(bool exhausted)
    {
        await using var collector = await TestCollector.StartAsync();
        using var app = CreateApp(collector.Endpoint, configure: (ax, db, _) =>
        {
            ax.SetupSequence(x => x.OpenDBConnectionAsync(It.IsAny<string>()))
                .ReturnsAsync(false).ReturnsAsync(false).ReturnsAsync(!exhausted);
            var table = new DataTable();
            table.Columns.Add("count", typeof(int));
            table.Rows.Add(0);
            db.Setup(x => x.ExecuteSQLAsync(It.IsAny<string>(), It.IsAny<string[]>(),
                It.IsAny<DbType[]>(), It.IsAny<object[]>())).ReturnsAsync(new SQLResult { data = table });
            db.Setup(x => x.ExecuteNonQueryAsync(It.IsAny<string>(), It.IsAny<string[]>(),
                It.IsAny<DbType[]>(), It.IsAny<object[]>())).ReturnsAsync(new NonQueryResult());
        });
        using var client = app.CreateClient(new() { BaseAddress = new Uri("https://localhost") });
        var response = await client.PostAsJsonAsync("/api/v1/Axi/setkeyfield", new
        {
            AppName = "PRIVATE_TENANT", Username = "PRIVATE_USER", TransId = "PRIVATE_FORM", KeyField = "PRIVATE_VALUE"
        });
        Assert.Equal(exhausted ? HttpStatusCode.BadRequest : HttpStatusCode.OK, response.StatusCode);
        await collector.WaitForAsync("/v1/metrics", "axi.database.retries");
        await collector.WaitForAsync("/v1/logs", "HTTP request completed");
        await collector.WaitForAsync("/v1/traces", "setkeyfield");
        if (exhausted) await collector.WaitForAsync("/v1/metrics", "axi.dependency.failures");
        Assert.Equal(2, collector.Counter("axi.database.retries"));
        Assert.Equal(exhausted ? 1 : 0, collector.Counter("axi.dependency.failures"));
        Assert.DoesNotContain("PRIVATE_", collector.Text);
    }

    [Theory]
    [InlineData("hit")]
    [InlineData("miss")]
    [InlineData("force_refresh")]
    [InlineData("successful_fallback")]
    [InlineData("write_failure")]
    public async Task Command_configuration_cache_outcomes_are_visible_and_private(string outcome)
    {
        await using var collector = await TestCollector.StartAsync();
        using var app = CreateApp(collector.Endpoint, configure: (ax, db, redis) =>
        {
            ax.Setup(x => x.OpenRedisConnectionAsync(It.IsAny<string>())).ReturnsAsync(true);
            var failure = new RedisConnectionException(ConnectionFailureType.UnableToConnect, "PRIVATE_EXCEPTION");
            var read = redis.Setup(x => x.StringGetAsync(It.IsAny<string>()));
            if (outcome == "successful_fallback") read.ThrowsAsync(failure);
            else read.ReturnsAsync(outcome == "hit" ? "[{\"ConfigId\":\"PRIVATE_COMMAND\"}]" : "");
            redis.Setup(x => x.KeysDeleteAsync(It.IsAny<RedisKey[]>())).ReturnsAsync(1);
            var write = redis.Setup(x => x.StringSetAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>()));
            if (outcome == "write_failure") write.ThrowsAsync(failure);
            else write.ReturnsAsync(true);
            var table = new DataTable();
            foreach (var column in new[] { "config_id", "command", "prompt_options", "prompt_id", "prompt_option_type",
                "param_field", "target_url", "extra_params", "active" }) table.Columns.Add(column);
            table.Rows.Add("PRIVATE_CONFIG", "PRIVATE_COMMAND", "option", "form", "tstruct", DBNull.Value,
                "https://example.test/?credential=PRIVATE_SECRET", DBNull.Value, "T");
            db.Setup(x => x.ExecuteSQLAsync(It.IsAny<string>(), It.IsAny<string[]>(),
                It.IsAny<DbType[]>(), It.IsAny<object[]>())).ReturnsAsync(new SQLResult { data = table });
        });
        using var client = app.CreateClient(new() { BaseAddress = new Uri("https://localhost") });
        var response = await client.GetAsync("/api/v1/Axi/command-config?appname=PRIVATE_TENANT&username=PRIVATE_USER" +
            (outcome == "force_refresh" ? "&forceRefresh=true" : ""));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await collector.WaitForAsync("/v1/metrics", outcome);
        await collector.WaitForAsync("/v1/logs", "HTTP request completed");
        await collector.WaitForAsync("/v1/traces", "command-config");
        Assert.Equal(1, collector.Counter("axi.cache.outcomes", ("axi.outcome", outcome)));
        Assert.Equal(0, collector.Counter("axi.dependency.failures"));
        Assert.DoesNotContain("PRIVATE_", collector.Text);
    }

    [Fact]
    public async Task Telemetry_disabled_preserves_response_and_exports_nothing()
    {
        await using var collector = await TestCollector.StartAsync();
        using var app = CreateApp(collector.Endpoint, enabled: false);
        using var client = app.CreateClient(new() { BaseAddress = new Uri("https://localhost") });
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(
            "/api/v1/Axi/command-config?appname=PRIVATE_TENANT&username=PRIVATE_USER")).StatusCode);
        app.Dispose();
        Assert.Empty(collector.Text);
    }

    [Fact]
    public async Task Cache_unavailable_falls_back_successfully_without_terminal_failure()
    {
        await using var collector = await TestCollector.StartAsync();
        using var app = CreateApp(collector.Endpoint);
        using var client = app.CreateClient(new() { BaseAddress = new Uri("https://localhost") });
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(
            "/api/v1/Axi/command-config?appname=PRIVATE_TENANT&username=PRIVATE_USER")).StatusCode);
        await collector.WaitForAsync("/v1/metrics", "successful_fallback");
        Assert.Equal(1, collector.Counter("axi.cache.outcomes"));
        Assert.Equal(0, collector.Counter("axi.dependency.failures"));
        Assert.DoesNotContain("PRIVATE_", collector.Text);
    }

    [Fact]
    public async Task Database_failure_returning_400_is_counted_and_has_safe_correlated_logs()
    {
        await using var collector = await TestCollector.StartAsync();
        using var app = CreateApp(collector.Endpoint, configure: (ax, _, _) =>
            ax.Setup(x => x.OpenDBConnectionAsync(It.IsAny<string>())).ReturnsAsync(false));
        using var client = app.CreateClient(new() { BaseAddress = new Uri("https://localhost") });
        var response = await client.GetAsync("/api/v1/Axi/command-config?appname=PRIVATE_TENANT&username=PRIVATE_USER");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await collector.WaitForAsync("/v1/metrics", "axi.dependency.failures");
        await collector.WaitForAsync("/v1/logs", "Request failed");
        await collector.WaitForAsync("/v1/traces", "command-config");
        Assert.Equal(1, collector.Counter("axi.dependency.failures"));
        var span = Assert.Single(collector.Spans, x => x.Number(6) == 2 && x.String(5).Contains("command-config"));
        Assert.Contains(collector.Logs, log => log.Bytes(9).SequenceEqual(span.Bytes(1)) &&
            log.Bytes(10).SequenceEqual(span.Bytes(2)));
        Assert.DoesNotContain("PRIVATE_", collector.Text);
    }

    [Fact]
    public async Task Normal_request_exports_route_metrics_and_trace_without_private_values()
    {
        await using var collector = await TestCollector.StartAsync();
        using var app = CreateApp(collector.Endpoint);
        using var client = app.CreateClient(new() { BaseAddress = new Uri("https://localhost") });
        client.DefaultRequestHeaders.Add("traceparent", "00-0123456789abcdef0123456789abcdef-0123456789abcdef-01");
        client.DefaultRequestHeaders.Add("tracestate", "customer=PRIVATE_TENANT");
        client.DefaultRequestHeaders.Add("baggage", "username=PRIVATE_USER");
        var response = await client.GetAsync("/api/v1/Axi/command-config?appname=PRIVATE_TENANT&username=PRIVATE_USER");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await collector.WaitForAsync("/v1/traces", "command-config");
        await collector.WaitForAsync("/v1/metrics", "axi.http.request.duration");
        Assert.DoesNotContain("PRIVATE_", collector.Text);
    }

    internal static WebApplicationFactory<AxiController> CreateApp(string endpoint, bool enabled = true,
        Action<Mock<IAxExtend>, Mock<IDbHelper>, Mock<IRedisCacheHelper>>? configure = null)
    {
        var ax = new Mock<IAxExtend>();
        var db = new Mock<IDbHelper>();
        var redis = new Mock<IRedisCacheHelper>();
        ax.Setup(x => x.OpenDBConnectionAsync(It.IsAny<string>())).ReturnsAsync(true);
        ax.Setup(x => x.OpenRedisConnectionAsync(It.IsAny<string>())).ReturnsAsync(false);
        ax.Setup(x => x.GetDB()).ReturnsAsync(db.Object);
        ax.Setup(x => x.GetRedis()).ReturnsAsync(redis.Object);
        db.Setup(x => x.ExecuteSQLAsync(It.IsAny<string>(), It.IsAny<string[]>(),
            It.IsAny<System.Data.DbType[]>(), It.IsAny<object[]>())).ReturnsAsync(new SQLResult());
        configure?.Invoke(ax, db, redis);
        return new WebApplicationFactory<AxiController>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            foreach (var setting in new Dictionary<string, string>
            {
                ["Observability:Enabled"] = enabled.ToString(),
                ["Observability:Endpoint"] = endpoint,
                ["Observability:MetricExportIntervalMilliseconds"] = "200",
                ["Observability:ExportTimeoutMilliseconds"] = "250",
                ["Observability:BatchDelayMilliseconds"] = "100",
                ["Observability:QueueSize"] = "32",
                ["Observability:BatchSize"] = "8",
                ["Serilog:WriteTo:0:Args:path"] = "/tmp/axiapi-tests/log.txt",
                ["Serilog:WriteTo:0:Args:restrictedToMinimumLevel"] = "Fatal",
                ["Serilog:WriteTo:1:Args:restrictedToMinimumLevel"] = "Fatal"
            }) builder.UseSetting(setting.Key, setting.Value);
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IAxExtend>();
                services.AddScoped(_ => ax.Object);
            });
        });
    }
}

internal sealed class TestCollector : IAsyncDisposable
{
    private readonly WebApplication app;
    private readonly ConcurrentQueue<(string Path, byte[] Body)> requests = new();
    private TestCollector(WebApplication app) => this.app = app;
    public string Endpoint { get; private set; } = "";
    public bool BlockExports { get; set; }
    public string Text => string.Join("\n", requests.Select(x => Encoding.Latin1.GetString(x.Body)));
    public IEnumerable<WireMessage> Spans => Records("/v1/traces");
    public IEnumerable<WireMessage> Logs => Records("/v1/logs");

    private IEnumerable<WireMessage> Records(string path) => requests.Where(x => x.Path == path)
        .SelectMany(x => WireMessage.Parse(x.Body).Messages(1))
        .SelectMany(x => x.Messages(2)).SelectMany(x => x.Messages(2));

    public long Counter(string name, params (string Key, string Value)[] attributes) => Records("/v1/metrics").Where(x => x.String(1) == name)
        .Select(x => x.Messages(7).Single().Messages(1)
            .Where(point => attributes.All(attribute => point.Messages(7).Any(pair => pair.String(1) == attribute.Key &&
                pair.Messages(2).Single().String(1) == attribute.Value)))
            .Sum(point => (long)point.Number(6)))
        .LastOrDefault();

    public static async Task<TestCollector> StartAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, 0));
        builder.Logging.ClearProviders();
        var app = builder.Build();
        var collector = new TestCollector(app);
        app.MapPost("/v1/{signal}", async context =>
        {
            using var buffer = new MemoryStream();
            await context.Request.Body.CopyToAsync(buffer);
            collector.requests.Enqueue((context.Request.Path, buffer.ToArray()));
            if (collector.BlockExports)
            {
                try { await Task.Delay(Timeout.InfiniteTimeSpan, context.RequestAborted); }
                catch (OperationCanceledException) { }
                return;
            }
            if (Environment.GetEnvironmentVariable("AXI_TEST_FORWARD_ENDPOINT") is { Length: > 0 } forward)
            {
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
                using var content = new ByteArrayContent(buffer.ToArray());
                content.Headers.ContentType = new("application/x-protobuf");
                using var result = await http.PostAsync(forward.TrimEnd('/') + context.Request.Path, content);
                result.EnsureSuccessStatusCode();
            }
            context.Response.ContentType = "application/x-protobuf";
            context.Response.StatusCode = 200;
        });
        await app.StartAsync();
        collector.Endpoint = app.Services.GetRequiredService<HttpServer>().Features
            .Get<IServerAddressesFeature>()!.Addresses.Single();
        return collector;
    }

    public async Task WaitForAsync(string path, string expected)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(12));
        while (!requests.Any(x => x.Path == path && Encoding.Latin1.GetString(x.Body).Contains(expected)))
            await Task.Delay(25, deadline.Token);
    }

    public async ValueTask DisposeAsync() => await app.DisposeAsync();
}

// Minimal OTLP wire reader for assertions at the receiving Collector, not SDK internals.
internal sealed class WireMessage
{
    private readonly List<(int Field, byte[]? Bytes, ulong Number)> fields = [];
    public IEnumerable<WireMessage> Messages(int field) => fields.Where(x => x.Field == field && x.Bytes is not null)
        .Select(x => Parse(x.Bytes!));
    public byte[] Bytes(int field) => fields.FirstOrDefault(x => x.Field == field).Bytes ?? [];
    public string String(int field) => Encoding.UTF8.GetString(Bytes(field));
    public ulong Number(int field) => fields.FirstOrDefault(x => x.Field == field).Number;

    public static WireMessage Parse(byte[] data)
    {
        var message = new WireMessage();
        var offset = 0;
        ulong Varint()
        {
            ulong value = 0;
            for (var shift = 0; shift < 64; shift += 7)
            {
                var next = data[offset++];
                value |= (ulong)(next & 127) << shift;
                if (next < 128) return value;
            }
            throw new InvalidDataException("Invalid protobuf varint");
        }
        while (offset < data.Length)
        {
            var tag = Varint();
            var field = (int)(tag >> 3);
            switch (tag & 7)
            {
                case 0: message.fields.Add((field, null, Varint())); break;
                case 1:
                    message.fields.Add((field, null, System.Buffers.Binary.BinaryPrimitives.ReadUInt64LittleEndian(data.AsSpan(offset, 8))));
                    offset += 8;
                    break;
                case 2:
                    var size = (int)Varint();
                    message.fields.Add((field, data.AsSpan(offset, size).ToArray(), 0));
                    offset += size;
                    break;
                case 5: offset += 4; break;
                default: throw new InvalidDataException("Unsupported protobuf field");
            }
        }
        return message;
    }
}
