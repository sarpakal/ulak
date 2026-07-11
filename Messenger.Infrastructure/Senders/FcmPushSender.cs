using System.Net.Http.Headers;
using Messenger.Core.Options;
using Microsoft.Extensions.Options;

namespace Messenger.Infrastructure.Senders;

/// <summary>
/// Sends push notifications via the FCM HTTP v1 API
/// (<c>POST /v1/projects/{projectId}/messages:send</c>) using an OAuth2 bearer token from
/// <see cref="IFcmAccessTokenProvider"/>. Replaces the legacy server-key API, which Google
/// has shut down.
/// </summary>
public class FcmPushSender : IPushNotificationSender
{
    private readonly FcmNotificationOptions _options;
    private readonly HttpClient _http;
    private readonly IFcmAccessTokenProvider _tokenProvider;

    public FcmPushSender(
        HttpClient httpClient,
        IOptions<FcmNotificationOptions> options,
        IFcmAccessTokenProvider tokenProvider)
    {
        _http = httpClient;
        _options = options.Value;
        _tokenProvider = tokenProvider;
    }

    public async Task SendAsync(PushMessage message, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_options.ProjectId))
            throw new InvalidOperationException(
                "FCM is not configured (set Fcm:ProjectId and Fcm:CredentialsPath).");

        var accessToken = await _tokenProvider.GetAccessTokenAsync(cancellationToken);

        var url = $"{_options.BaseUrl.TrimEnd('/')}/v1/projects/{_options.ProjectId}/messages:send";
        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = JsonContent.Create(new
            {
                message = new
                {
                    token = message.To,
                    notification = new
                    {
                        title = message.Title,
                        body = message.Body
                    }
                }
            })
        };
        // Set auth per-request (not on DefaultRequestHeaders — the typed client is shared).
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        var response = await _http.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
    }
}
