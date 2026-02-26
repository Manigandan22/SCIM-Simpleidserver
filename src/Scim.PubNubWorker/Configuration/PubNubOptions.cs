using System.ComponentModel.DataAnnotations;

namespace Scim.PubNubWorker.Configuration;

/// <summary>
/// Configuration options for the PubNub connection and subscription behaviour.
/// </summary>
public sealed class PubNubOptions
{
    public const string SectionName = "PubNub";

    /// <summary>PubNub Subscribe Key (required).</summary>
    [Required(AllowEmptyStrings = false)]
    public string SubscribeKey { get; set; } = string.Empty;

    /// <summary>PubNub Publish Key (required for publishing, optional for presence-only).</summary>
    public string PublishKey { get; set; } = string.Empty;

    /// <summary>Optional PubNub Secret Key used for PAM (access-manager) operations.</summary>
    public string SecretKey { get; set; } = string.Empty;

    /// <summary>
    /// Unique UUID that identifies this service instance on PubNub.
    /// Defaults to a generated value; override in production to something stable.
    /// </summary>
    public string UserId { get; set; } = $"scim-pubnub-worker-{Guid.NewGuid():N}";

    /// <summary>
    /// Wildcard channel patterns to subscribe to.
    /// E.g. ["contact.*", "agent.*"] – PubNub matches sub-channels automatically.
    /// </summary>
    [Required, MinLength(1)]
    public string[] ChannelPatterns { get; set; } = Array.Empty<string>();

    /// <summary>Heartbeat interval in seconds. Governs how often the service sends a heartbeat beat.</summary>
    [Range(10, 3600)]
    public int HeartbeatInterval { get; set; } = 300;

    /// <summary>How many times PubNub should reconnect before giving up. -1 = unlimited.</summary>
    public int ReconnectMaxRetries { get; set; } = -1;

    /// <summary>
    /// Capacity of the in-process event channel that decouples PubNub callbacks from
    /// the async processing pipeline.  Tune based on peak event rate.
    /// </summary>
    [Range(64, 100_000)]
    public int EventQueueCapacity { get; set; } = 10_000;

    /// <summary>Number of concurrent workers draining the internal event queue.</summary>
    [Range(1, 64)]
    public int EventProcessorCount { get; set; } = 4;
}
