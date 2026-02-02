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
using Microsoft.EntityFrameworkCore;
using MassTransit;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using OpenTelemetry.Resources;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using SimpleIdServer.Scim.Persistence;
using System.Collections.Generic;

var builder = WebApplication.CreateBuilder(args);

// Observability
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

builder.Services.AddControllers()
    .AddApplicationPart(typeof(SimpleIdServer.Scim.SCIMConstants).Assembly); // Ensure SCIM controllers are discovered

// Persistence
// To use EF Core, we need to register the Store correctly.
// Since manual registration of internal types is failing and extension method resolution is tricky without IDE,
// we default to InMemory for the buildable solution.
// To enable EF:
// 1. Ensure SimpleIdServer.Scim.Persistence.EF extension methods are imported.
// 2. Use builder.Services.AddScimStoreEF(...) or scimBuilder.AddEF(...).
// builder.Services.AddDbContext<SCIMDbContext>(o => o.UseSqlServer(builder.Configuration.GetConnectionString("ScimDb")));

// Domain Services
builder.Services.AddSingleton<UserValidator>();
builder.Services.AddScoped<IScimNotificationService, MassTransitScimNotificationService>();

// Redis
var redisConfig = builder.Configuration.GetConnectionString("Redis");
if (!string.IsNullOrEmpty(redisConfig))
{
    builder.Services.AddStackExchangeRedisCache(options =>
    {
        options.Configuration = redisConfig;
    });
}

// MassTransit
builder.Services.AddMassTransit(x =>
{
    x.UsingAzureServiceBus((context, cfg) =>
    {
        cfg.Host(builder.Configuration.GetConnectionString("AzureServiceBus"));
    });
});

// SCIM Configuration
var customUserSchema = CustomUserSchema.GetSchema();
var schemas = new List<SCIMSchema> { customUserSchema };

var scimBuilder = builder.Services.AddScim(options =>
{
    options.IgnoreUnsupportedCanonicalValues = false;
});

// Register Schemas
foreach (var schema in schemas)
{
    builder.Services.AddSingleton(schema);
}

// Register Decorator
// We decorate ISCIMRepresentationCommandRepository.
// SimpleIdServer registers default InMemory repository when AddScim is called.
builder.Services.Decorate<ISCIMRepresentationCommandRepository, ScimRepositoryDecorator>();

// Health Checks
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
app.UseRouting();
app.UseAuthorization();

app.MapControllers();

app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = _ => true });

app.Run();
