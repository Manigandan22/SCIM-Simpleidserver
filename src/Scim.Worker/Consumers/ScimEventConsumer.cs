using MassTransit;
using Microsoft.Extensions.Logging;
using Scim.Contracts;
using Scim.Domain.Services;
using System;
using System.Threading.Tasks;

namespace Scim.Worker.Consumers
{
    public class ScimEventConsumer :
        IConsumer<RepresentationAddedEvent>,
        IConsumer<RepresentationUpdatedEvent>,
        IConsumer<RepresentationRemovedEvent>
    {
        private readonly ILogger<ScimEventConsumer> _logger;
        private readonly IIdempotencyService _idempotencyService;

        public ScimEventConsumer(ILogger<ScimEventConsumer> logger, IIdempotencyService idempotencyService)
        {
            _logger = logger;
            _idempotencyService = idempotencyService;
        }

        public async Task Consume(ConsumeContext<RepresentationAddedEvent> context)
        {
            var message = context.Message;
            var key = $"event:{message.Id}:{message.Version}:added"; // Idempotency key

            if (!await _idempotencyService.AcquireLockAsync(key, TimeSpan.FromHours(1)))
            {
                _logger.LogInformation("Skipping duplicate event Added {ResourceId} Version {Version}", message.Id, message.Version);
                return;
            }

            _logger.LogInformation("Processing RepresentationAddedEvent for {ResourceType} {Id}", message.ResourceType, message.Id);
            // Simulate business logic (Projection update)
            await Task.Delay(100);
        }

        public async Task Consume(ConsumeContext<RepresentationUpdatedEvent> context)
        {
            var message = context.Message;
            var key = $"event:{message.Id}:{message.Version}:updated";

            if (!await _idempotencyService.AcquireLockAsync(key, TimeSpan.FromHours(1)))
            {
                _logger.LogInformation("Skipping duplicate event Updated {ResourceId} Version {Version}", message.Id, message.Version);
                return;
            }

            _logger.LogInformation("Processing RepresentationUpdatedEvent for {ResourceType} {Id}", message.ResourceType, message.Id);
            // Logic
            await Task.Delay(100);
        }

        public async Task Consume(ConsumeContext<RepresentationRemovedEvent> context)
        {
            var message = context.Message;
            var key = $"event:{message.Id}:removed:{message.Timestamp.Ticks}"; // No version on delete usually, use timestamp or just ID if only once

            if (!await _idempotencyService.AcquireLockAsync(key, TimeSpan.FromHours(1)))
            {
                _logger.LogInformation("Skipping duplicate event Removed {ResourceId}", message.Id);
                return;
            }

            _logger.LogInformation("Processing RepresentationRemovedEvent for {ResourceType} {Id}", message.ResourceType, message.Id);
            // Logic
            await Task.Delay(100);
        }
    }
}
