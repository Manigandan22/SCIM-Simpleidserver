using MassTransit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Scim.Domain.Services;
using Scim.Infrastructure.Caching;
using Scim.Worker.Consumers;
using StackExchange.Redis;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using OpenTelemetry.Metrics;
using Microsoft.Extensions.Configuration;

var builder = Host.CreateApplicationBuilder(args);

// Observability
builder.Services.AddOpenTelemetry()
    .WithTracing(tracing => tracing
        .SetResourceBuilder(ResourceBuilder.CreateDefault().AddService("Scim.Worker"))
        .AddSource("MassTransit")
        .AddConsoleExporter())
    .WithMetrics(metrics => metrics
        .SetResourceBuilder(ResourceBuilder.CreateDefault().AddService("Scim.Worker"))
        .AddConsoleExporter());

// Redis
var redisConfig = builder.Configuration.GetConnectionString("Redis") ?? "localhost:6379";
builder.Services.AddSingleton<IConnectionMultiplexer>(ConnectionMultiplexer.Connect(redisConfig));
builder.Services.AddSingleton<IIdempotencyService, RedisIdempotencyService>();

// MassTransit
builder.Services.AddMassTransit(x =>
{
    x.AddConsumer<ScimEventConsumer>();

    x.UsingAzureServiceBus((context, cfg) =>
    {
        cfg.Host(builder.Configuration.GetConnectionString("AzureServiceBus"));

        // Use default subscription name logic or explicit
        // cfg.SubscriptionEndpoint("scim-updates", "scim-updates-sub", e => { ... });

        cfg.ConfigureEndpoints(context);

        // Resilience
        cfg.UseMessageRetry(r => r.Exponential(3, System.TimeSpan.FromSeconds(1), System.TimeSpan.FromSeconds(30), System.TimeSpan.FromSeconds(2)));
    });
});

var host = builder.Build();
host.Run();
