using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using Scim.Shared.Services;
using Scim.Shared.Models;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Scim.Worker
{
    public class ServiceBusWorker : BackgroundService
    {
        private readonly ILogger<ServiceBusWorker> _logger;
        private readonly ICacheService _cacheService;
        private readonly ServiceBusClient _client;
        private readonly ServiceBusProcessor _processor;

        public ServiceBusWorker(ILogger<ServiceBusWorker> logger, ICacheService cacheService, IConfiguration configuration)
        {
            _logger = logger;
            _cacheService = cacheService;
            var connectionString = configuration.GetConnectionString("ServiceBus");
            var topicName = configuration["ServiceBus:TopicName"] ?? "scim-events";
            var subscriptionName = configuration["ServiceBus:SubscriptionName"] ?? "worker-sub";

            // In a real app, you would probably DI the client or factory
            _client = new ServiceBusClient(connectionString);
            _processor = _client.CreateProcessor(topicName, subscriptionName, new ServiceBusProcessorOptions());
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _processor.ProcessMessageAsync += MessageHandler;
            _processor.ProcessErrorAsync += ErrorHandler;

            await _processor.StartProcessingAsync(stoppingToken);

            _logger.LogInformation("ServiceBusWorker started processing messages.");

            // Wait until stoppingToken is cancelled
            try
            {
                await Task.Delay(Timeout.Infinite, stoppingToken);
            }
            catch (TaskCanceledException)
            {
                // Task cancelled, proceed to cleanup
            }

            await _processor.StopProcessingAsync();
            await _processor.CloseAsync();
            await _client.DisposeAsync();
        }

        private async Task MessageHandler(ProcessMessageEventArgs args)
        {
            string body = args.Message.Body.ToString();
            _logger.LogInformation($"Received message: {body}");

            try
            {
                var notification = JsonSerializer.Deserialize<ScimNotification>(body);
                if (notification != null)
                {
                    // Example processing: Cache the latest action for the resource
                    await _cacheService.SetAsync($"last-action:{notification.ResourceId}", notification.Action);
                    _logger.LogInformation($"Processed action {notification.Action} for {notification.ResourceType} {notification.ResourceId}");
                }
            }
            catch (JsonException ex)
            {
                _logger.LogError(ex, "Error deserializing message");
            }

            await args.CompleteMessageAsync(args.Message);
        }

        private Task ErrorHandler(ProcessErrorEventArgs args)
        {
            _logger.LogError(args.Exception, "Message handler encountered an exception");
            return Task.CompletedTask;
        }
    }
}
