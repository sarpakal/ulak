namespace Messenger.Infrastructure.Senders;

/// <summary>
/// Abstraction over SMS providers.
/// Implementations: CorvassSmsSender, TwilioSmsSender, ConsoleSmsService (dev).
/// Routing across providers is handled by RoutingSmsSender.
/// </summary>
public interface ISmsService
{
    /// <summary>
    /// Sends an SMS to the given E.164 phone number.
    /// Returns true on success. Throws <see cref="SmsException"/> on failure after retries.
    /// </summary>
    Task<bool> SendAsync(string phoneNumber, string text, CancellationToken ct = default);
}

/// <summary>Thrown when an SMS send fails after all retry attempts.</summary>
public class SmsException : Exception
{
    public string PhoneNumber { get; }
    public string ProviderName { get; }

    public SmsException(string phoneNumber, string providerName, string message, Exception? inner = null)
        : base(message, inner)
    {
        PhoneNumber  = phoneNumber;
        ProviderName = providerName;
    }
}
