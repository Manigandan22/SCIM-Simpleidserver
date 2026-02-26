using System.ComponentModel.DataAnnotations;

namespace Scim.PubNubWorker.Configuration;

/// <summary>
/// Configuration options for calling the NICE CXone/inContact signal API.
/// </summary>
public sealed class NiceApiOptions
{
    public const string SectionName = "NiceApi";

    /// <summary>Base URL of the NICE API (e.g. https://api.nicecxone.com).</summary>
    [Required(AllowEmptyStrings = false)]
    [Url]
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>
    /// Path template for the signal endpoint.
    /// Use {contactId} as the placeholder; it is replaced at runtime.
    /// </summary>
    [Required(AllowEmptyStrings = false)]
    public string SignalEndpointTemplate { get; set; } = "/api/v1/contacts/{contactId}/signal";

    /// <summary>API key sent in the X-API-Key header.</summary>
    [Required(AllowEmptyStrings = false)]
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>Optional Bearer / OAuth token; preferred over ApiKey when provided.</summary>
    public string BearerToken { get; set; } = string.Empty;

    /// <summary>HTTP request timeout in seconds.</summary>
    [Range(5, 300)]
    public int TimeoutSeconds { get; set; } = 30;

    /// <summary>Maximum retry attempts on transient failures (exponential back-off).</summary>
    [Range(0, 10)]
    public int MaxRetryAttempts { get; set; } = 3;

    /// <summary>Base delay in seconds for the first retry. Subsequent retries double this value.</summary>
    [Range(1, 60)]
    public int RetryBaseDelaySeconds { get; set; } = 2;

    /// <summary>Number of failures before the circuit breaker opens.</summary>
    [Range(2, 50)]
    public int CircuitBreakerFailureThreshold { get; set; } = 5;

    /// <summary>Duration in seconds the circuit remains open before a half-open probe attempt.</summary>
    [Range(5, 600)]
    public int CircuitBreakerDurationSeconds { get; set; } = 30;

    /// <summary>
    /// Signal type/reason sent in the request body to inform NICE why the contact departed.
    /// </summary>
    public string SignalReason { get; set; } = "AgentPresenceExpired";
}
