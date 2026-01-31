using System.Threading;
using System.Threading.Tasks;
using Scim.Contracts;

namespace Scim.Domain.Services
{
    public interface IScimNotificationService
    {
        Task NotifyAddedAsync(RepresentationAddedEvent @event, CancellationToken token);
        Task NotifyUpdatedAsync(RepresentationUpdatedEvent @event, CancellationToken token);
        Task NotifyRemovedAsync(RepresentationRemovedEvent @event, CancellationToken token);
    }
}
