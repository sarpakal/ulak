namespace Messenger.Infrastructure.Senders;

/// <summary>
/// Supplies an OAuth2 access token for the FCM HTTP v1 API. Abstracted so the token-minting
/// (Google service-account credential) is swappable in tests.
/// </summary>
public interface IFcmAccessTokenProvider
{
    Task<string> GetAccessTokenAsync(CancellationToken cancellationToken = default);
}
