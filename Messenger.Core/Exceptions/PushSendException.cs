namespace Messenger.Core.Exceptions;

/// <summary>
/// Classified outcome of a failed push send, used by callers to decide what to do
/// with the device token and the request.
/// </summary>
public enum PushSendFailureReason
{
    /// <summary>Dead or malformed device token — safe for the caller to delete it.</summary>
    InvalidToken,

    /// <summary>Provider throttling persisted after retries — caller should back off.</summary>
    QuotaExceeded,

    /// <summary>Provider/network/other failure — retryable later, not the token's fault.</summary>
    ProviderError
}

/// <summary>
/// Thrown by <see cref="Interfaces.IPushNotificationSender"/> implementations when a push
/// send fails after transport-level retries are exhausted. Carries the classified reason
/// plus provider detail so the API layer can map it to an honest HTTP response
/// (410 dead token / 429 throttled / 502 provider error).
/// </summary>
public class PushSendException : Exception
{
    public PushSendFailureReason Reason { get; }

    /// <summary>Provider error code, e.g. "UNREGISTERED", "QUOTA_EXCEEDED" (null if unparsable).</summary>
    public string? ProviderErrorCode { get; }

    /// <summary>Final upstream HTTP status code after retries (null for transport failures).</summary>
    public int? HttpStatusCode { get; }

    /// <summary>Backoff hint from the provider's Retry-After header, if present.</summary>
    public TimeSpan? RetryAfter { get; }

    public PushSendException(
        PushSendFailureReason reason,
        string message,
        string? providerErrorCode = null,
        int? httpStatusCode = null,
        TimeSpan? retryAfter = null,
        Exception? innerException = null)
        : base(message, innerException)
    {
        Reason = reason;
        ProviderErrorCode = providerErrorCode;
        HttpStatusCode = httpStatusCode;
        RetryAfter = retryAfter;
    }
}
