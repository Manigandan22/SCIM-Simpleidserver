using System.Threading.Tasks;
using Scim.Shared.Models;

namespace Scim.Shared.Services
{
    public interface IServiceBusPublisher
    {
        Task PublishAsync(ScimNotification notification);
    }
}
