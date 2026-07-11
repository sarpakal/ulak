using Google.Apis.Auth.OAuth2;
using Messenger.Core.Options;
using Microsoft.Extensions.Options;

namespace Messenger.Infrastructure.Senders;

/// <summary>
/// Mints FCM HTTP v1 access tokens from a Google service-account credential. Registered as a
/// singleton: the <see cref="GoogleCredential"/> is created once (lazily) and caches/refreshes
/// the underlying access token internally, so we don't re-read the key file per send.
/// </summary>
public sealed class GoogleFcmAccessTokenProvider : IFcmAccessTokenProvider
{
    private const string FirebaseMessagingScope = "https://www.googleapis.com/auth/firebase.messaging";

    private readonly FcmNotificationOptions _options;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private GoogleCredential? _credential;

    public GoogleFcmAccessTokenProvider(IOptions<FcmNotificationOptions> options)
        => _options = options.Value;

    public async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken = default)
    {
        var credential = await GetCredentialAsync(cancellationToken);
        // GetAccessTokenForRequestAsync is an explicit ITokenAccess member on GoogleCredential.
        return await ((ITokenAccess)credential).GetAccessTokenForRequestAsync(cancellationToken: cancellationToken);
    }

    private async Task<GoogleCredential> GetCredentialAsync(CancellationToken cancellationToken)
    {
        if (_credential is not null)
            return _credential;

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_credential is null)
            {
                if (string.IsNullOrWhiteSpace(_options.CredentialsPath))
                    throw new InvalidOperationException(
                        "FCM credentials are not configured (set Fcm:CredentialsPath to a service-account JSON file).");

                _credential = GoogleCredential
                    .FromFile(_options.CredentialsPath)
                    .CreateScoped(FirebaseMessagingScope);
            }

            return _credential;
        }
        finally
        {
            _gate.Release();
        }
    }
}
