using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SimpleIdServer.Scim;
using Scim.Api.Schemas;
using Scim.Shared.Services;
using StackExchange.Redis;
using Azure.Messaging.ServiceBus;
using Scim.Api.Services;
using SimpleIdServer.Scim.Persistence;
using Scim.Api.Validators;
using Microsoft.Extensions.Configuration;
using SimpleIdServer.Scim.Domains;
using System.Collections.Generic;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllers();

// Redis
var redisConnectionString = builder.Configuration.GetConnectionString("Redis") ?? "localhost:6379";
builder.Services.AddSingleton<IConnectionMultiplexer>(ConnectionMultiplexer.Connect(redisConnectionString));
builder.Services.AddSingleton<ICacheService, RedisCacheService>();

// Service Bus
var sbConnectionString = builder.Configuration.GetConnectionString("ServiceBus");
var topicName = builder.Configuration["ServiceBus:TopicName"] ?? "scim-events";
if (!string.IsNullOrEmpty(sbConnectionString))
{
    builder.Services.AddSingleton(new ServiceBusClient(sbConnectionString));
    builder.Services.AddSingleton<IServiceBusPublisher>(sp =>
        new AzureServiceBusPublisher(sp.GetRequiredService<ServiceBusClient>(), topicName));
}
else
{
    // Mock for build/test without config
    // throw new System.Exception("Service Bus Connection String missing");
}

// Validator
builder.Services.AddSingleton<UserValidator>();

// SCIM
var customUserSchema = CustomSchemas.GetUserSchema();

builder.Services.AddScim(options =>
{
    options.IgnoreUnsupportedCanonicalValues = false;
});

builder.Services.AddSingleton(customUserSchema); // Registering custom schema in DI might be enough or requires specific registration

// Decorate the repository to publish events
builder.Services.Decorate<ISCIMRepresentationCommandRepository, ScimEventPublisher>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
}

app.UseHttpsRedirection();
app.UseRouting();
app.UseAuthorization();

app.MapControllers();

app.Run();
