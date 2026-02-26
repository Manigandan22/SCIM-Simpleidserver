using System.ComponentModel.DataAnnotations;

namespace Scim.PubNubWorker.Configuration;

/// <summary>
/// Configuration options governing how contact presence state is stored in Redis.
/// </summary>
public sealed class RedisPresenceOptions
{
    public const string SectionName = "RedisPresence";

    /// <summary>
    /// TTL applied to a contact key when a Leave event is received.
    /// After this period the key expires and the NICE API signal is triggered.
    /// </summary>
    [Range(1, 3600)]
    public int ContactTtlSeconds { get; set; } = 5;

    /// <summary>Redis key prefix for contact-presence keys.</summary>
    [Required(AllowEmptyStrings = false)]
    public string KeyPrefix { get; set; } = "presence:contact:";

    /// <summary>Redis key prefix used for distributed processing locks.</summary>
    [Required(AllowEmptyStrings = false)]
    public string LockPrefix { get; set; } = "lock:presence:contact:";

    /// <summary>
    /// TTL for the distributed processing lock acquired before calling NICE API.
    /// Should be comfortably longer than the NICE API timeout.
    /// </summary>
    [Range(5, 600)]
    public int LockTtlSeconds { get; set; } = 60;

    /// <summary>Redis logical database index (0 by default).</summary>
    [Range(0, 15)]
    public int Database { get; set; } = 0;
}
