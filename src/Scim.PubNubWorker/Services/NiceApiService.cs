using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Scim.PubNubWorker.Configuration;
using Scim.PubNubWorker.Services.Interfaces;

namespace Scim.PubNubWorker.Services;

/// <summary>
/// Typed HTTP client for the NICE CXone signal API.
/// Resilience (retry + circuit-breaker) is configured on the <see cref="HttpClient"/>
/// via <c>Microsoft.Extensions.Http.Resilience</c> in the DI setup.
/// </summary>
internal sealed class NiceApiService : INiceApiService
{
    private readonly HttpClient _httpClient;
    private readonly NiceApiOptions _options;
    private readonly ILogger<NiceApiService> _logger;

    public NiceApiService(
        HttpClient httpClient,
        IOptions<NiceApiOptions> options,
        ILogger<NiceApiService> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
    }

    // -----------------------------------------------------------------------
    // INiceApiService
    // -----------------------------------------------------------------------

    /// <inheritdoc />
    public async Task SignalContactDepartureAsync(
        string contactId,
        CancellationToken cancellationToken)
    {
        var path = _options.SignalEndpointTemplate
            .Replace("{contactId}", Uri.EscapeDataString(contactId), StringComparison.OrdinalIgnoreCase);

        var payload = new
        {
            contactId,
            reason = _options.SignalReason,
            timestamp = DateTimeOffset.UtcNow
        };

        _logger.LogInformation(
            "Sending departure signal to NICE API for contact {ContactId} via {Path}",
            contactId, path);

        HttpResponseMessage response;
        try
        {
            response = await _httpClient
                .PostAsJsonAsync(path, payload, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex,
                "Network error calling NICE API for contact {ContactId}",
                contactId);
            throw;
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogError(ex,
                "NICE API request timed out for contact {ContactId}",
                contactId);
            throw new TimeoutException(
                $"NICE API request timed out for contact {contactId}.", ex);
        }

        if (response.IsSuccessStatusCode)
        {
            _logger.LogInformation(
                "NICE API acknowledged departure signal for contact {ContactId} ({StatusCode})",
                contactId, (int)response.StatusCode);
            return;
        }

        // 4xx errors are non-retryable (bad request, auth failure, contact not found).
        if (response.StatusCode is >= HttpStatusCode.BadRequest and < HttpStatusCode.InternalServerError)
        {
            var body = await SafeReadBodyAsync(response, cancellationToken).ConfigureAwait(false);
            _logger.LogError(
                "NICE API returned client error {StatusCode} for contact {ContactId}. Body: {Body}",
                (int)response.StatusCode, contactId, body);

            // Do not retry client errors.
            throw new InvalidOperationException(
                $"NICE API returned {(int)response.StatusCode} for contact {contactId}: {body}");
        }

        // 5xx – retryable; throw so the resilience pipeline retries.
        {
            var body = await SafeReadBodyAsync(response, cancellationToken).ConfigureAwait(false);
            _logger.LogWarning(
                "NICE API returned server error {StatusCode} for contact {ContactId}. Body: {Body}",
                (int)response.StatusCode, contactId, body);

            response.EnsureSuccessStatusCode(); // throws HttpRequestException → retried
        }
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static async Task<string> SafeReadBodyAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        try
        {
            return await response.Content
                .ReadAsStringAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        catch
        {
            return "<unreadable>";
        }
    }
}
