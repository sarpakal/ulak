using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Messenger.Core.Exceptions;
using Messenger.Core.Options;
using Microsoft.Extensions.Options;
using Polly.Timeout;

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
                "FCM is not configured (set Messaging:FcmNotification:ProjectId and " +
                "Messaging:FcmNotification:CredentialsPath — env keys " +
                "Messaging__FcmNotification__ProjectId / __CredentialsPath).");

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

        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or TimeoutRejectedException)
        {
            // Transport failure after the resilience pipeline exhausted its retries.
            // OperationCanceledException (caller cancellation) intentionally propagates.
            throw new PushSendException(
                PushSendFailureReason.ProviderError,
                $"FCM send failed (transport): {ex.Message}",
                innerException: ex);
        }

        using (response)
        {
            if (response.IsSuccessStatusCode)
                return;

            // Retries are already exhausted by the resilience handler — classify the final failure.
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            var truncatedBody = body.Length > 2048 ? body[..2048] : body;
            var fcmErrorCode = ParseFcmErrorCode(body);
            var retryAfter = response.Headers.RetryAfter switch
            {
                { Delta: { } delta } => delta,
                { Date: { } date } => date - DateTimeOffset.UtcNow,
                _ => (TimeSpan?)null
            };

            var (reason, errorCode) = ClassifyFailure(response.StatusCode, fcmErrorCode);
            throw new PushSendException(
                reason,
                $"FCM send failed ({(int)response.StatusCode} {errorCode ?? "?"}): {truncatedBody}",
                errorCode,
                (int)response.StatusCode,
                retryAfter);
        }
    }

    /// <summary>
    /// Extracts the FCM-specific error code from an HTTP v1 error payload:
    /// <c>error.details[]</c> entry with <c>@type = type.googleapis.com/google.firebase.fcm.v1.FcmError</c>,
    /// falling back to <c>error.status</c>. Never throws — unparsable bodies yield null.
    /// </summary>
    private static string? ParseFcmErrorCode(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (!doc.RootElement.TryGetProperty("error", out var error))
                return null;

            if (error.TryGetProperty("details", out var details) && details.ValueKind == JsonValueKind.Array)
            {
                foreach (var detail in details.EnumerateArray())
                {
                    if (detail.TryGetProperty("@type", out var type)
                        && type.GetString() == "type.googleapis.com/google.firebase.fcm.v1.FcmError"
                        && detail.TryGetProperty("errorCode", out var code))
                    {
                        return code.GetString();
                    }
                }
            }

            return error.TryGetProperty("status", out var status) ? status.GetString() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static (PushSendFailureReason Reason, string? ErrorCode) ClassifyFailure(
        HttpStatusCode status, string? fcmErrorCode)
    {
        // Dead token — safe for the caller to delete it.
        if (fcmErrorCode == "UNREGISTERED" || status == HttpStatusCode.NotFound)
            return (PushSendFailureReason.InvalidToken, "UNREGISTERED");

        // Malformed token: ULAK builds the payload itself, so the only caller-supplied
        // field that can be invalid is the token.
        if (fcmErrorCode == "INVALID_ARGUMENT" || status == HttpStatusCode.BadRequest)
            return (PushSendFailureReason.InvalidToken, "INVALID_ARGUMENT");

        // Persistent throttle (the pipeline already retried honoring Retry-After).
        if (fcmErrorCode == "QUOTA_EXCEEDED" || status == HttpStatusCode.TooManyRequests)
            return (PushSendFailureReason.QuotaExceeded, "QUOTA_EXCEEDED");

        // SENDER_ID_MISMATCH (403) is a credential/project mismatch — an ULAK config
        // problem, not a dead token; callers must NOT delete tokens over it.
        // Everything else (UNAVAILABLE, INTERNAL, 5xx, unparsable) is a provider fault.
        return (PushSendFailureReason.ProviderError, fcmErrorCode);
    }
}
