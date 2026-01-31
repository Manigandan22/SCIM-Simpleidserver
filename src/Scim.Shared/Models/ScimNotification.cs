namespace Scim.Shared.Models
{
    public class ScimNotification
    {
        public string ResourceType { get; set; } = string.Empty;
        public string Action { get; set; } = string.Empty; // Create, Update, Delete
        public string Payload { get; set; } = string.Empty; // JSON representation
        public string ResourceId { get; set; } = string.Empty;
    }
}
