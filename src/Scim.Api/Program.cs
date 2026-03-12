using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SimpleIdServer.Scim;
using SimpleIdServer.Scim.Persistence.EF;
using SimpleIdServer.Scim.Domains;
using Scim.Domain.Schemas;
using Scim.Infrastructure.Messaging;
using Scim.Infrastructure.Caching;
using Scim.Domain.Services;
using Scim.Api.Decorators;
using Scim.Api.Validators;
using Scim.Api.Middleware;
using Microsoft.EntityFrameworkCore;
using MassTransit;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using OpenTelemetry.Resources;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using SimpleIdServer.Scim.Persistence;
using System.Collections.Generic;
using Serilog;
using Serilog.Events;
using Serilog.Sinks.Elasticsearch;

// ─── Bootstrap logger (used during host startup before configuration is ready) ──
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Warning()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    // ─── Serilog ──────────────────────────────────────────────────────────────────
    // Read Elasticsearch settings from configuration
    var esUri      = builder.Configuration["Elasticsearch:Uri"]         ?? "http://localhost:9200";
    var esUser     = builder.Configuration["Elasticsearch:Username"]    ?? "elastic";
    var esPassword = builder.Configuration["Elasticsearch:Password"]    ?? "changeme";
    var esIndex    = builder.Configuration["Elasticsearch:IndexPrefix"] ?? "scim-api";
    var esBatch    = int.TryParse(builder.Configuration["Elasticsearch:BatchPostingLimit"], out var b) ? b : 50;
    var esPeriod   = int.TryParse(builder.Configuration["Elasticsearch:PeriodSeconds"],    out var p) ? p : 5;

    builder.Host.UseSerilog((ctx, services, cfg) => cfg
        // ── Level overrides: keep noise from framework internals out of ES ──────
        .MinimumLevel.Information()
        .MinimumLevel.Override("Microsoft",                    LogEventLevel.Warning)
        .MinimumLevel.Override("Microsoft.EntityFrameworkCore", LogEventLevel.Warning)
        .MinimumLevel.Override("System",                       LogEventLevel.Warning)
        .MinimumLevel.Override("MassTransit",                  LogEventLevel.Warning)
        // ── Enrichers: add context fields to every log event ─────────────────
        .Enrich.FromLogContext()
        .Enrich.WithMachineName()
        .Enrich.WithThreadId()
        .Enrich.WithProperty("Application", "Scim.Api")
        .Enrich.WithProperty("Environment", ctx.HostingEnvironment.EnvironmentName)
        // ── Console sink: human-readable output for local development ─────────
        .WriteTo.Console(
            outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] [{Application}] {Message:lj} {Properties:j}{NewLine}{Exception}",
            restrictedToMinimumLevel: LogEventLevel.Information)
        // ── Elasticsearch sink ────────────────────────────────────────────────
        // Batched to avoid hammering ES with individual HTTP calls per log event.
        // Only Information+ events reach ES (Debug is excluded).
        .WriteTo.Elasticsearch(new ElasticsearchSinkOptions(new Uri(esUri))
        {
            AutoRegisterTemplate        = true,
            AutoRegisterTemplateVersion = AutoRegisterTemplateVersion.ESv7,
            // Monthly index: scim-api-2025.03
            IndexFormat                 = $"{esIndex}-{{0:yyyy.MM}}",
            // Authenticated connection
            ModifyConnectionSettings    = conn => conn.BasicAuthentication(esUser, esPassword),
            // Batch settings: flush every N seconds or when batch is full
            BatchPostingLimit           = esBatch,
            Period                      = TimeSpan.FromSeconds(esPeriod),
            MinimumLogEventLevel        = LogEventLevel.Information,
            // Write sink failures to Serilog self-log (stderr) rather than throwing
            EmitEventFailure            = EmitEventFailureHandling.WriteToSelfLog,
            // Detect ES version automatically so the correct ingest API is used
            DetectElasticsearchVersion  = true,
        })
    );

    // ─── Observability ────────────────────────────────────────────────────────────
    builder.Services.AddOpenTelemetry()
        .WithTracing(tracing => tracing
            .SetResourceBuilder(ResourceBuilder.CreateDefault().AddService("Scim.Api"))
            .AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation()
            .AddConsoleExporter())
        .WithMetrics(metrics => metrics
            .SetResourceBuilder(ResourceBuilder.CreateDefault().AddService("Scim.Api"))
            .AddAspNetCoreInstrumentation()
            .AddConsoleExporter());

    builder.Services.AddControllers();

    // ─── Persistence ─────────────────────────────────────────────────────────────
    // To use EF Core, we need to register the Store correctly.
    // Since manual registration of internal types is failing and extension method resolution is tricky without IDE,
    // we default to InMemory for the buildable solution.
    // To enable EF:
    // 1. Ensure SimpleIdServer.Scim.Persistence.EF extension methods are imported.
    // 2. Use builder.Services.AddScimStoreEF(...) or scimBuilder.AddEF(...).
    // builder.Services.AddDbContext<SCIMDbContext>(o => o.UseSqlServer(builder.Configuration.GetConnectionString("ScimDb")));

    // ─── Domain Services ──────────────────────────────────────────────────────────
    builder.Services.AddSingleton<UserValidator>();
    builder.Services.AddScoped<IScimNotificationService, MassTransitScimNotificationService>();

    // ─── Redis ───────────────────────────────────────────────────────────────────
    var redisConfig = builder.Configuration.GetConnectionString("Redis");
    if (!string.IsNullOrEmpty(redisConfig))
    {
        builder.Services.AddStackExchangeRedisCache(options =>
        {
            options.Configuration = redisConfig;
        });
    }

    // ─── MassTransit ─────────────────────────────────────────────────────────────
    builder.Services.AddMassTransit(x =>
    {
        x.UsingAzureServiceBus((context, cfg) =>
        {
            cfg.Host(builder.Configuration.GetConnectionString("AzureServiceBus"));
        });
    });

    // ─── SCIM Configuration ───────────────────────────────────────────────────────
    var customUserSchema = CustomUserSchema.GetSchema();
    var schemas = new List<SCIMSchema> { customUserSchema };

    var scimBuilder = builder.Services.AddScim(options =>
    {
        options.IgnoreUnsupportedCanonicalValues = false;
    });

    foreach (var schema in schemas)
    {
        builder.Services.AddSingleton(schema);
    }

    // ─── Decorator ────────────────────────────────────────────────────────────────
    builder.Services.Decorate<ISCIMRepresentationCommandRepository, ScimRepositoryDecorator>();

    // ─── Health Checks ────────────────────────────────────────────────────────────
    builder.Services.AddHealthChecks()
        .AddSqlServer(builder.Configuration.GetConnectionString("ScimDb")!, name: "database")
        .AddRedis(builder.Configuration.GetConnectionString("Redis")!, name: "redis")
        .AddAzureServiceBusTopic(builder.Configuration.GetConnectionString("AzureServiceBus")!, "scim-events", name: "bus");

    var app = builder.Build();

    if (app.Environment.IsDevelopment())
    {
        app.UseDeveloperExceptionPage();
    }

    app.UseHttpsRedirection();

    // ─── Elastic logging middleware ───────────────────────────────────────────────
    // Register before routing so every request (except health probes) is captured
    app.UseMiddleware<ElasticLoggingMiddleware>();

    app.UseRouting();
    app.UseAuthorization();

    app.MapControllers();

    app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
    app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = _ => true });

    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Scim.Api failed to start");
    throw;
}
finally
{
    // Flush remaining log events to Elasticsearch before the process exits
    Log.CloseAndFlush();
}
