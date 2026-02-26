using System.Threading.Channels;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PubnubApi;
using Scim.PubNubWorker.Configuration;
using Scim.PubNubWorker.Services.Interfaces;

namespace Scim.PubNubWorker.Services;

/// <summary>
/// Long-running background service that:
/// <list type="number">
///   <item>Connects to PubNub and subscribes to configured channel patterns with presence.</item>
///   <item>Receives presence callbacks on a thread pool thread from the PubNub SDK.</item>
///   <item>Offloads events to a bounded <see cref="Channel{T}"/> for back-pressure control.</item>
///   <item>Spawns a configurable number of async workers to drain and process the channel.</item>
/// </list>
/// </summary>
internal sealed class PubNubListenerService : BackgroundService
{
    // -----------------------------------------------------------------------
    // Internal event record written to the channel
    // -----------------------------------------------------------------------

    private sealed record PresenceEvent(
        string EventType,   // "join", "leave", "timeout"
        string Channel,
        string ContactId,
        long Timestamp);

    // -----------------------------------------------------------------------
    // Fields
    // -----------------------------------------------------------------------

    private readonly PubNubOptions _options;
    private readonly IContactPresenceHandler _presenceHandler;
    private readonly ILogger<PubNubListenerService> _logger;

    private readonly Channel<PresenceEvent> _eventChannel;
    private Pubnub? _pubnub;

    // -----------------------------------------------------------------------
    // Constructor
    // -----------------------------------------------------------------------

    public PubNubListenerService(
        IOptions<PubNubOptions> options,
        IContactPresenceHandler presenceHandler,
        ILogger<PubNubListenerService> logger)
    {
        _options = options.Value;
        _presenceHandler = presenceHandler;
        _logger = logger;

        _eventChannel = Channel.CreateBounded<PresenceEvent>(
            new BoundedChannelOptions(_options.EventQueueCapacity)
            {
                FullMode = BoundedChannelFullMode.DropOldest,   // shed load gracefully
                SingleWriter = false,
                SingleReader = false
            });
    }

    // -----------------------------------------------------------------------
    // BackgroundService
    // -----------------------------------------------------------------------

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("PubNubListenerService starting…");

        InitialisePubNub(stoppingToken);

        // Start consumer workers in parallel.
        var workers = Enumerable
            .Range(0, _options.EventProcessorCount)
            .Select(i => ProcessEventsAsync(i, stoppingToken))
            .ToArray();

        try
        {
            await Task.WhenAll(workers).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("PubNubListenerService stopped.");
        }
        catch (Exception ex)
        {
            _logger.LogCritical(ex, "PubNubListenerService encountered a fatal error.");
            throw;
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("PubNubListenerService stopping; unsubscribing from PubNub…");

        if (_pubnub is not null)
        {
            try
            {
                _pubnub.Unsubscribe<string>()
                    .Channels(_options.ChannelPatterns)
                    .Execute();

                // Brief pause to allow in-flight callbacks to drain.
                await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken)
                    .ConfigureAwait(false);

                _pubnub.Destroy();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error while unsubscribing from PubNub.");
            }
        }

        _eventChannel.Writer.TryComplete();
        await base.StopAsync(cancellationToken).ConfigureAwait(false);
    }

    // -----------------------------------------------------------------------
    // PubNub initialisation
    // -----------------------------------------------------------------------

    private void InitialisePubNub(CancellationToken stoppingToken)
    {
        var config = new PNConfiguration(new UserId(_options.UserId))
        {
            SubscribeKey = _options.SubscribeKey,
            PublishKey   = _options.PublishKey,
            SecretKey    = _options.SecretKey,
            Secure       = true,
            HeartbeatNotificationOption = PNHeartbeatNotificationOption.All,
            PresenceTimeout = _options.HeartbeatInterval,
            ReconnectionPolicy = PNReconnectionPolicy.EXPONENTIAL
        };

        _pubnub = new Pubnub(config);

        var listener = new SubscribeCallbackExt(
            messageCallback: (_, _) => { /* presence-only; messages are ignored */ },
            presenceCallback: (_, presence) => OnPresenceEvent(presence, stoppingToken),
            statusCallback: (_, status) => OnStatusEvent(status, stoppingToken));

        _pubnub.AddListener(listener);

        _pubnub.Subscribe<string>()
            .Channels(_options.ChannelPatterns)
            .WithPresence()
            .Execute();

        _logger.LogInformation(
            "Subscribed to PubNub channels {Patterns} with presence",
            string.Join(", ", _options.ChannelPatterns));
    }

    // -----------------------------------------------------------------------
    // PubNub callbacks (synchronous, SDK-managed thread)
    // -----------------------------------------------------------------------

    private void OnPresenceEvent(PNPresenceEventResult presence, CancellationToken stoppingToken)
    {
        if (stoppingToken.IsCancellationRequested) return;

        var eventType = presence.Event?.ToLowerInvariant() ?? "unknown";

        if (eventType is not ("join" or "leave" or "timeout"))
        {
            _logger.LogDebug(
                "Ignoring PubNub presence event '{EventType}' on channel {Channel}",
                eventType, presence.Channel);
            return;
        }

        // PubNub 'interval' events include a list of joins/leaves; handle individually.
        if (presence.Join?.Length > 0)
        {
            foreach (var uuid in presence.Join)
                EnqueuePresenceEvent("join", presence.Channel, uuid, presence.Timestamp);
        }
        else if (presence.Leave?.Length > 0)
        {
            foreach (var uuid in presence.Leave)
                EnqueuePresenceEvent("leave", presence.Channel, uuid, presence.Timestamp);
        }
        else if (presence.Timeout?.Length > 0)
        {
            foreach (var uuid in presence.Timeout)
                EnqueuePresenceEvent("timeout", presence.Channel, uuid, presence.Timestamp);
        }
        else
        {
            EnqueuePresenceEvent(eventType, presence.Channel, presence.Uuid, presence.Timestamp);
        }
    }

    private void EnqueuePresenceEvent(string eventType, string channel, string contactId, long timestamp)
    {
        if (string.IsNullOrWhiteSpace(contactId))
        {
            _logger.LogWarning(
                "Received PubNub {EventType} event on channel {Channel} with empty contactId – ignored",
                eventType, channel);
            return;
        }

        var ev = new PresenceEvent(eventType, channel, contactId, timestamp);

        if (!_eventChannel.Writer.TryWrite(ev))
        {
            _logger.LogWarning(
                "Event channel full – dropped {EventType} event for contact {ContactId} on channel {Channel}",
                eventType, contactId, channel);
        }
    }

    private void OnStatusEvent(PNStatus status, CancellationToken stoppingToken)
    {
        if (status.Error)
        {
            _logger.LogError(
                "PubNub status error: {Category} – {StatusCode}",
                status.Category, status.StatusCode);
        }
        else
        {
            _logger.LogInformation(
                "PubNub status: {Category} – {Operation}",
                status.Category, status.Operation);
        }
    }

    // -----------------------------------------------------------------------
    // Event processing workers
    // -----------------------------------------------------------------------

    private async Task ProcessEventsAsync(int workerId, CancellationToken stoppingToken)
    {
        _logger.LogDebug("Presence event worker #{WorkerId} started.", workerId);

        await foreach (var ev in _eventChannel.Reader.ReadAllAsync(stoppingToken).ConfigureAwait(false))
        {
            try
            {
                await DispatchEventAsync(ev, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // Log but keep the worker alive; a single bad event must not kill the pipeline.
                _logger.LogError(ex,
                    "Worker #{WorkerId}: unhandled error processing {EventType} for contact {ContactId} on {Channel}",
                    workerId, ev.EventType, ev.ContactId, ev.Channel);
            }
        }

        _logger.LogDebug("Presence event worker #{WorkerId} stopped.", workerId);
    }

    private Task DispatchEventAsync(PresenceEvent ev, CancellationToken ct) =>
        ev.EventType switch
        {
            "join"    => _presenceHandler.HandleJoinAsync(ev.Channel, ev.ContactId, ct),
            "leave"   => _presenceHandler.HandleLeaveAsync(ev.Channel, ev.ContactId, ct),
            "timeout" => _presenceHandler.HandleLeaveAsync(ev.Channel, ev.ContactId, ct),
            _         => Task.CompletedTask
        };
}
