namespace Scim.PubNubWorker.Services.Interfaces;

/// <summary>
/// Sends signals to the NICE CXone API when a contact's presence expires.
/// </summary>
public interface INiceApiService
{
    /// <summary>
    /// Notifies NICE that the contact identified by <paramref name="contactId"/> has
    /// disconnected / timed-out.
    /// </summary>
    /// <param name="contactId">The contact identifier known to NICE.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task SignalContactDepartureAsync(string contactId, CancellationToken cancellationToken);
}
