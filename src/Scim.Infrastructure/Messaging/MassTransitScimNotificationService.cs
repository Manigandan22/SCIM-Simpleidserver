using MassTransit;
using Scim.Contracts;
using Scim.Domain.Services;
using System.Threading;
using System.Threading.Tasks;

namespace Scim.Infrastructure.Messaging
{
    public class MassTransitScimNotificationService : IScimNotificationService
    {
        private readonly IPublishEndpoint _publishEndpoint;

        public MassTransitScimNotificationService(IPublishEndpoint publishEndpoint)
        {
            _publishEndpoint = publishEndpoint;
        }

        public Task NotifyAddedAsync(RepresentationAddedEvent @event, CancellationToken token)
        {
            return _publishEndpoint.Publish(@event, token);
        }

        public Task NotifyUpdatedAsync(RepresentationUpdatedEvent @event, CancellationToken token)
        {
            return _publishEndpoint.Publish(@event, token);
        }

        public Task NotifyRemovedAsync(RepresentationRemovedEvent @event, CancellationToken token)
        {
            return _publishEndpoint.Publish(@event, token);
        }
    }
}
