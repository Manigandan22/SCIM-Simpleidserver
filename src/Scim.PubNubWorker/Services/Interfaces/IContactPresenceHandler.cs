namespace Scim.PubNubWorker.Services.Interfaces;

/// <summary>
/// Handles PubNub presence events for a contact.
/// </summary>
public interface IContactPresenceHandler
{
    /// <summary>
    /// Called when a contact joins a channel.
    /// Purges any existing leave-entry from Redis so stale TTLs don't fire.
    /// </summary>
    /// <param name="channel">The PubNub channel name.</param>
    /// <param name="contactId">The PubNub UUID / contact identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task HandleJoinAsync(string channel, string contactId, CancellationToken cancellationToken);

    /// <summary>
    /// Called when a contact leaves (or times-out on) a channel.
    /// Stores a Redis key with a short TTL that triggers the NICE API on expiry.
    /// </summary>
    /// <param name="channel">The PubNub channel name.</param>
    /// <param name="contactId">The PubNub UUID / contact identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task HandleLeaveAsync(string channel, string contactId, CancellationToken cancellationToken);
}
