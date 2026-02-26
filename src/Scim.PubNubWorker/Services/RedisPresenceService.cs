using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Scim.PubNubWorker.Configuration;
using Scim.PubNubWorker.Services.Interfaces;
using StackExchange.Redis;

namespace Scim.PubNubWorker.Services;

/// <summary>
/// Manages contact-presence keys in Redis.
/// Keyspace expiry notifications trigger the NICE API signal.
/// </summary>
internal sealed class RedisPresenceService : IRedisPresenceService
{
    private readonly IConnectionMultiplexer _redis;
    private readonly RedisPresenceOptions _options;
    private readonly ILogger<RedisPresenceService> _logger;

    public RedisPresenceService(
        IConnectionMultiplexer redis,
        IOptions<RedisPresenceOptions> options,
        ILogger<RedisPresenceService> logger)
    {
        _redis = redis;
        _options = options.Value;
        _logger = logger;
    }

    // -----------------------------------------------------------------------
    // Key helpers
    // -----------------------------------------------------------------------

    private string PresenceKey(string contactId) =>
        $"{_options.KeyPrefix}{contactId}";

    private string LockKey(string contactId) =>
        $"{_options.LockPrefix}{contactId}";

    // -----------------------------------------------------------------------
    // IRedisPresenceService
    // -----------------------------------------------------------------------

    /// <inheritdoc />
    public async Task PurgeContactAsync(string contactId, CancellationToken cancellationToken)
    {
        var db = _redis.GetDatabase(_options.Database);
        var key = PresenceKey(contactId);

        bool deleted = await db.KeyDeleteAsync(key).ConfigureAwait(false);

        if (deleted)
        {
            _logger.LogInformation(
                "Purged Redis presence key {Key} for contact {ContactId}",
                key, contactId);
        }
        else
        {
            _logger.LogDebug(
                "No Redis presence key found to purge for contact {ContactId}",
                contactId);
        }
    }

    /// <inheritdoc />
    public async Task SetContactWithTtlAsync(string contactId, CancellationToken cancellationToken)
    {
        var db = _redis.GetDatabase(_options.Database);
        var key = PresenceKey(contactId);
        var ttl = TimeSpan.FromSeconds(_options.ContactTtlSeconds);

        // SET key contactId EX ttl – overwrite any previous entry to reset the timer.
        await db.StringSetAsync(key, contactId, ttl).ConfigureAwait(false);

        _logger.LogInformation(
            "Stored Redis presence key {Key} for contact {ContactId} with TTL {Ttl}s",
            key, contactId, _options.ContactTtlSeconds);
    }

    /// <inheritdoc />
    public async Task<bool> TryAcquireProcessingLockAsync(
        string contactId,
        CancellationToken cancellationToken)
    {
        var db = _redis.GetDatabase(_options.Database);
        var lockKey = LockKey(contactId);
        var lockTtl = TimeSpan.FromSeconds(_options.LockTtlSeconds);

        // SET NX EX – atomic; succeeds only for the first caller in a cluster.
        bool acquired = await db.StringSetAsync(
            lockKey,
            Environment.MachineName,
            lockTtl,
            When.NotExists).ConfigureAwait(false);

        if (acquired)
        {
            _logger.LogDebug(
                "Acquired processing lock {LockKey} for contact {ContactId}",
                lockKey, contactId);
        }
        else
        {
            _logger.LogDebug(
                "Could not acquire processing lock {LockKey} for contact {ContactId} – another instance is handling it",
                lockKey, contactId);
        }

        return acquired;
    }
}
