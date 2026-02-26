using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Scim.PubNubWorker.Configuration;
using Scim.PubNubWorker.Services.Interfaces;
using StackExchange.Redis;

namespace Scim.PubNubWorker.Services;

/// <summary>
/// Background service that:
/// <list type="number">
///   <item>Enables Redis keyspace expiry notifications on startup (<c>Ex</c> flags).</item>
///   <item>Subscribes to the <c>__keyevent@{db}__:expired</c> pub/sub channel.</item>
///   <item>Filters for keys whose names start with the configured presence key prefix.</item>
///   <item>Extracts the <c>contactId</c> and calls NICE API — acquiring a distributed lock
///         first to prevent duplicate processing when multiple service instances run.</item>
/// </list>
/// </summary>
internal sealed class RedisKeyExpirationService : BackgroundService
{
    // -----------------------------------------------------------------------
    // Fields
    // -----------------------------------------------------------------------

    private readonly IConnectionMultiplexer _redis;
    private readonly INiceApiService _niceApi;
    private readonly IRedisPresenceService _redisPresence;
    private readonly RedisPresenceOptions _presenceOptions;
    private readonly ILogger<RedisKeyExpirationService> _logger;

    private ISubscriber? _subscriber;

    // -----------------------------------------------------------------------
    // Constructor
    // -----------------------------------------------------------------------

    public RedisKeyExpirationService(
        IConnectionMultiplexer redis,
        INiceApiService niceApi,
        IRedisPresenceService redisPresence,
        IOptions<RedisPresenceOptions> presenceOptions,
        ILogger<RedisKeyExpirationService> logger)
    {
        _redis = redis;
        _niceApi = niceApi;
        _redisPresence = redisPresence;
        _presenceOptions = presenceOptions.Value;
        _logger = logger;
    }

    // -----------------------------------------------------------------------
    // BackgroundService
    // -----------------------------------------------------------------------

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("RedisKeyExpirationService starting…");

        await EnableKeyspaceNotificationsAsync().ConfigureAwait(false);
        await SubscribeToExpiryEventsAsync(stoppingToken).ConfigureAwait(false);

        // Keep the service alive until the host shuts down.
        await stoppingToken.AsTask().ConfigureAwait(false);
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("RedisKeyExpirationService stopping…");

        if (_subscriber is not null)
        {
            try
            {
                await _subscriber
                    .UnsubscribeAllAsync()
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error while unsubscribing Redis keyspace listener.");
            }
        }

        await base.StopAsync(cancellationToken).ConfigureAwait(false);
    }

    // -----------------------------------------------------------------------
    // Redis setup
    // -----------------------------------------------------------------------

    /// <summary>
    /// Configures <c>notify-keyspace-events</c> to publish expiry events.
    /// <para>
    /// Flag legend: <c>K</c> = keyspace, <c>E</c> = keyevent, <c>x</c> = expired.
    /// We use <c>Ex</c> (keyevent + expired) — lowest overhead, no keyspace noise.
    /// </para>
    /// NOTE: On managed Redis (e.g. Azure Cache for Redis, ElastiCache) this CONFIG SET
    /// call may be blocked by the provider.  In that case, enable it via the portal/CLI
    /// and remove this call.
    /// </summary>
    private async Task EnableKeyspaceNotificationsAsync()
    {
        try
        {
            var server = _redis.GetServers().FirstOrDefault()
                ?? throw new InvalidOperationException("No Redis servers found.");

            await server.ConfigSetAsync("notify-keyspace-events", "Ex")
                .ConfigureAwait(false);

            _logger.LogInformation(
                "Redis keyspace notifications set to 'Ex' (expiry events) on {Endpoint}",
                server.EndPoint);
        }
        catch (Exception ex)
        {
            // On managed Redis this is expected (CONFIG SET is disabled).
            // Log as warning; the subscription will still work if the provider has
            // already enabled the flag via its configuration panel.
            _logger.LogWarning(ex,
                "Could not configure Redis keyspace notifications via CONFIG SET. " +
                "Ensure 'notify-keyspace-events' includes 'Ex' in your Redis configuration.");
        }
    }

    private async Task SubscribeToExpiryEventsAsync(CancellationToken stoppingToken)
    {
        // __keyevent@{db}__:expired  → fires whenever any key in the given DB expires.
        var channel = new RedisChannel(
            $"__keyevent@{_presenceOptions.Database}__:expired",
            RedisChannel.PatternMode.Literal);

        _subscriber = _redis.GetSubscriber();

        await _subscriber.SubscribeAsync(channel, (_, keyName) =>
        {
            // Callbacks are synchronous per StackExchange.Redis design.
            // Fire-and-forget onto the thread pool with proper error handling.
            if (stoppingToken.IsCancellationRequested) return;

            string key = keyName.ToString() ?? string.Empty;
            _ = HandleExpiredKeyAsync(key, stoppingToken);
        }).ConfigureAwait(false);

        _logger.LogInformation(
            "Subscribed to Redis keyevent channel for database {Database}",
            _presenceOptions.Database);
    }

    // -----------------------------------------------------------------------
    // Key-expiry handler
    // -----------------------------------------------------------------------

    private async Task HandleExpiredKeyAsync(string key, CancellationToken stoppingToken)
    {
        // Only process our own presence keys; ignore everything else.
        if (!key.StartsWith(_presenceOptions.KeyPrefix, StringComparison.Ordinal))
            return;

        var contactId = key[_presenceOptions.KeyPrefix.Length..];

        if (string.IsNullOrWhiteSpace(contactId))
        {
            _logger.LogWarning("Expired Redis key '{Key}' has an empty contactId – skipped.", key);
            return;
        }

        _logger.LogInformation(
            "Redis presence key expired for contact {ContactId} – preparing NICE API signal",
            contactId);

        try
        {
            // Distributed lock prevents duplicate NICE API calls when multiple
            // service replicas receive the same keyevent notification.
            bool lockAcquired = await _redisPresence
                .TryAcquireProcessingLockAsync(contactId, stoppingToken)
                .ConfigureAwait(false);

            if (!lockAcquired)
            {
                _logger.LogDebug(
                    "Skipping NICE API call for contact {ContactId} – another instance is handling it.",
                    contactId);
                return;
            }

            await _niceApi
                .SignalContactDepartureAsync(contactId, stoppingToken)
                .ConfigureAwait(false);

            _logger.LogInformation(
                "Successfully signalled NICE API for contact {ContactId}",
                contactId);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            _logger.LogDebug(
                "NICE API signal for contact {ContactId} cancelled during shutdown.",
                contactId);
        }
        catch (Exception ex)
        {
            // Log with full context.  The lock TTL will expire naturally, allowing
            // a retry on a future restart — or the ops team can investigate.
            _logger.LogError(ex,
                "Failed to signal NICE API for contact {ContactId} after Redis key expiry.",
                contactId);
        }
    }
}

// ---------------------------------------------------------------------------
// Extension to convert CancellationToken → Task (waitable until cancelled)
// ---------------------------------------------------------------------------

file static class CancellationTokenExtensions
{
    internal static Task AsTask(this CancellationToken ct)
    {
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        ct.Register(() => tcs.TrySetCanceled(), useSynchronizationContext: false);
        return tcs.Task;
    }
}
