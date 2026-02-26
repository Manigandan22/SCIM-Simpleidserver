using Microsoft.Extensions.Logging;
using Scim.PubNubWorker.Services.Interfaces;

namespace Scim.PubNubWorker.Services;

/// <summary>
/// Orchestrates the business rules for PubNub presence events:
/// <list type="bullet">
///   <item><b>Join</b> – purge any pending leave-TTL in Redis so a spurious NICE signal is not sent.</item>
///   <item><b>Leave / Timeout</b> – write a Redis key with TTL; expiry triggers the NICE API.</item>
/// </list>
/// </summary>
internal sealed class ContactPresenceHandler : IContactPresenceHandler
{
    private readonly IRedisPresenceService _redisPresence;
    private readonly ILogger<ContactPresenceHandler> _logger;

    public ContactPresenceHandler(
        IRedisPresenceService redisPresence,
        ILogger<ContactPresenceHandler> logger)
    {
        _redisPresence = redisPresence;
        _logger = logger;
    }

    // -----------------------------------------------------------------------
    // IContactPresenceHandler
    // -----------------------------------------------------------------------

    /// <inheritdoc />
    public async Task HandleJoinAsync(
        string channel,
        string contactId,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "JOIN event on channel {Channel} for contact {ContactId}",
            channel, contactId);

        try
        {
            // If a Leave was received recently and the TTL key is still alive, delete it
            // so the NICE API is NOT triggered for a transient disconnect.
            await _redisPresence.PurgeContactAsync(contactId, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Non-fatal: the contact has joined, but we failed to clear a potential leave key.
            // Log with full context so on-call can investigate.
            _logger.LogError(ex,
                "Failed to purge Redis key on JOIN for contact {ContactId} on channel {Channel}",
                contactId, channel);
        }
    }

    /// <inheritdoc />
    public async Task HandleLeaveAsync(
        string channel,
        string contactId,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "LEAVE/TIMEOUT event on channel {Channel} for contact {ContactId}",
            channel, contactId);

        try
        {
            // Store the key with TTL. When Redis expires it, the keyspace notification
            // listener (RedisKeyExpirationService) will call the NICE API.
            await _redisPresence.SetContactWithTtlAsync(contactId, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to set Redis presence key on LEAVE for contact {ContactId} on channel {Channel}",
                contactId, channel);

            // Re-throw so the caller can decide on retry / dead-letter strategy.
            throw;
        }
    }
}
