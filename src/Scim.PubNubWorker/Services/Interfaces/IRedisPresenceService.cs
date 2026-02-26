namespace Scim.PubNubWorker.Services.Interfaces;

/// <summary>
/// Manages contact-presence keys in Redis used for leave-event TTL tracking.
/// </summary>
public interface IRedisPresenceService
{
    /// <summary>
    /// Removes the presence key for <paramref name="contactId"/> so an in-flight TTL
    /// cannot fire the NICE API after the contact has re-joined.
    /// </summary>
    Task PurgeContactAsync(string contactId, CancellationToken cancellationToken);

    /// <summary>
    /// Stores a presence key for <paramref name="contactId"/> with the configured TTL.
    /// When Redis expires this key a keyspace notification is published and the
    /// NICE API will be called.
    /// </summary>
    Task SetContactWithTtlAsync(string contactId, CancellationToken cancellationToken);

    /// <summary>
    /// Attempts to acquire a short-lived distributed lock so that only one service
    /// instance processes the expiration signal for a given contact.
    /// </summary>
    /// <returns><c>true</c> if the lock was acquired; <c>false</c> if another instance holds it.</returns>
    Task<bool> TryAcquireProcessingLockAsync(string contactId, CancellationToken cancellationToken);
}
