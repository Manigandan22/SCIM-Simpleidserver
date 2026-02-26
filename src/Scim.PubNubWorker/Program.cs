using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Scim.PubNubWorker.Configuration;
using Scim.PubNubWorker.Services;
using Scim.PubNubWorker.Services.Interfaces;
using StackExchange.Redis;

var builder = Host.CreateApplicationBuilder(args);

// ---------------------------------------------------------------------------
// Configuration validation
// ---------------------------------------------------------------------------

builder.Services
    .AddOptions<PubNubOptions>()
    .BindConfiguration(PubNubOptions.SectionName)
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services
    .AddOptions<NiceApiOptions>()
    .BindConfiguration(NiceApiOptions.SectionName)
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services
    .AddOptions<RedisPresenceOptions>()
    .BindConfiguration(RedisPresenceOptions.SectionName)
    .ValidateDataAnnotations()
    .ValidateOnStart();

// ---------------------------------------------------------------------------
// Redis
// ---------------------------------------------------------------------------

builder.Services.AddSingleton<IConnectionMultiplexer>(sp =>
{
    var redisCs = builder.Configuration.GetConnectionString("Redis")
        ?? "localhost:6379";

    var redisConfig = ConfigurationOptions.Parse(redisCs);
    redisConfig.AbortOnConnectFail = false;       // allow reconnects
    redisConfig.ReconnectRetryPolicy = new ExponentialRetry(
        deltaBackOffMilliseconds: 1_000,
        maxDeltaBackOffMilliseconds: 30_000);

    return ConnectionMultiplexer.Connect(redisConfig);
});

// ---------------------------------------------------------------------------
// Application services
// ---------------------------------------------------------------------------

builder.Services.AddSingleton<IRedisPresenceService, RedisPresenceService>();
builder.Services.AddSingleton<IContactPresenceHandler, ContactPresenceHandler>();

// ---------------------------------------------------------------------------
// NICE API typed HttpClient with resilience
// ---------------------------------------------------------------------------

// Read NICE API config values directly from IConfiguration (avoids building a
// second service provider just to resolve IOptions inside a registration delegate).
var niceSection = builder.Configuration.GetSection(NiceApiOptions.SectionName);
var niceBaseUrl         = niceSection["BaseUrl"] ?? string.Empty;
var niceApiKey          = niceSection["ApiKey"] ?? string.Empty;
var niceBearerToken     = niceSection["BearerToken"] ?? string.Empty;
var niceTimeoutSec      = int.TryParse(niceSection["TimeoutSeconds"], out var ts) ? ts : 30;
var niceMaxRetry        = int.TryParse(niceSection["MaxRetryAttempts"], out var mr) ? mr : 3;
var niceRetryDelaySec   = int.TryParse(niceSection["RetryBaseDelaySeconds"], out var rd) ? rd : 2;
var niceCbDurationSec   = int.TryParse(niceSection["CircuitBreakerDurationSeconds"], out var cb) ? cb : 30;

builder.Services
    .AddHttpClient<INiceApiService, NiceApiService>(client =>
    {
        if (!string.IsNullOrWhiteSpace(niceBaseUrl))
            client.BaseAddress = new Uri(niceBaseUrl);

        client.Timeout = TimeSpan.FromSeconds(niceTimeoutSec);

        // Prefer Bearer token; fall back to API key header.
        if (!string.IsNullOrWhiteSpace(niceBearerToken))
        {
            client.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", niceBearerToken);
        }
        else if (!string.IsNullOrWhiteSpace(niceApiKey))
        {
            client.DefaultRequestHeaders.Add("X-API-Key", niceApiKey);
        }
    })
    .AddStandardResilienceHandler(options =>
    {
        // Microsoft.Extensions.Http.Resilience wraps Polly v8 under the hood.
        options.Retry.MaxRetryAttempts = niceMaxRetry;
        options.Retry.Delay = TimeSpan.FromSeconds(niceRetryDelaySec);
        options.Retry.UseJitter = true;

        options.CircuitBreaker.SamplingDuration =
            TimeSpan.FromSeconds(niceCbDurationSec * 2);
        options.CircuitBreaker.BreakDuration =
            TimeSpan.FromSeconds(niceCbDurationSec);
    });

// ---------------------------------------------------------------------------
// Background services
// ---------------------------------------------------------------------------

builder.Services.AddHostedService<PubNubListenerService>();
builder.Services.AddHostedService<RedisKeyExpirationService>();

// ---------------------------------------------------------------------------
// Health checks
// ---------------------------------------------------------------------------

builder.Services
    .AddHealthChecks()
    .AddRedis(
        sp => sp.GetRequiredService<IConnectionMultiplexer>(),
        name: "redis",
        failureStatus: HealthStatus.Degraded,
        tags: new[] { "ready" });

// ---------------------------------------------------------------------------
// Observability (OpenTelemetry)
// ---------------------------------------------------------------------------

builder.Services.AddOpenTelemetry()
    .ConfigureResource(r => r
        .AddService(
            serviceName: "Scim.PubNubWorker",
            serviceVersion: typeof(Program).Assembly.GetName().Version?.ToString()))
    .WithTracing(tracing => tracing
        .AddHttpClientInstrumentation()
        .AddConsoleExporter())
    .WithMetrics(metrics => metrics
        .AddHttpClientInstrumentation()
        .AddConsoleExporter());

// ---------------------------------------------------------------------------
// Build & run
// ---------------------------------------------------------------------------

var host = builder.Build();
await host.RunAsync();
