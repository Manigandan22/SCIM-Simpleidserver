using Azure.Messaging.ServiceBus;
using System.Text.Json;
using System.Threading.Tasks;
using Scim.Shared.Models;

namespace Scim.Shared.Services
{
    public class AzureServiceBusPublisher : IServiceBusPublisher
    {
        private readonly ServiceBusSender _sender;

        public AzureServiceBusPublisher(ServiceBusClient client, string queueOrTopicName)
        {
            _sender = client.CreateSender(queueOrTopicName);
        }

        public async Task PublishAsync(ScimNotification notification)
        {
            var json = JsonSerializer.Serialize(notification);
            var message = new ServiceBusMessage(json);
            await _sender.SendMessageAsync(message);
        }
    }
}
