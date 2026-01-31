using System;

namespace Scim.Contracts
{
    public record RepresentationAddedEvent
    {
        public string Id { get; init; } = null!;
        public string ResourceType { get; init; } = null!;
        public string RepresentationJson { get; init; } = null!;
        public string Version { get; init; } = null!;
        public DateTime Timestamp { get; init; } = DateTime.UtcNow;
    }

    public record RepresentationUpdatedEvent
    {
        public string Id { get; init; } = null!;
        public string ResourceType { get; init; } = null!;
        public string RepresentationJson { get; init; } = null!; // Or Patch operations
        public string Version { get; init; } = null!;
        public DateTime Timestamp { get; init; } = DateTime.UtcNow;
    }

    public record RepresentationRemovedEvent
    {
        public string Id { get; init; } = null!;
        public string ResourceType { get; init; } = null!;
        public DateTime Timestamp { get; init; } = DateTime.UtcNow;
    }
}
