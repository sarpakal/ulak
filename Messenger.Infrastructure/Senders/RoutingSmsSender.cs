using Messenger.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Messenger.Infrastructure.Senders;

/// <summary>
/// Routes outgoing SMS to the correct provider based on the recipient's E.164 prefix.
///
/// Routing table (from appsettings.json → Sms:ProviderPrefixes):
///   +90 → Corvass  (Turkish mobile lines)
///   +1  → Twilio   (US/CA lines)
///
/// Recipients with different prefixes in a single message are split and dispatched separately.
/// Falls back to ConsoleSmsService if no prefix matches (dev safety net).
/// Throws <see cref="SmsException"/> if all retry attempts fail for any group.
/// </summary>
public class RoutingSmsSender : ISmsSender
{
    private readonly CorvassSmsSender _corvass;
    private readonly TwilioSmsSender _twilio;
    private readonly ConsoleSmsService _console;
    private readonly SmsOptions _options;
    private readonly ILogger<RoutingSmsSender> _logger;

    public RoutingSmsSender(
        CorvassSmsSender corvass,
        TwilioSmsSender twilio,
        ConsoleSmsService console,
        IOptions<SmsOptions> options,
        ILogger<RoutingSmsSender> logger)
    {
        _corvass = corvass;
        _twilio  = twilio;
        _console = console;
        _options = options.Value;
        _logger  = logger;
    }

    public async Task SendAsync(SmsMessage message, CancellationToken cancellationToken = default)
    {
        // Group recipients by provider so a mixed-prefix batch fans out correctly
        var groups = message.To.GroupBy(n => _options.ResolveProvider(n) ?? "Console");

        foreach (var group in groups)
        {
            var providerName = group.Key;
            var sender = Resolve(providerName);
            var batch = message with { To = group.ToList() };
            await SendWithRetryAsync(sender, batch, providerName, cancellationToken);
        }
    }

    private async Task SendWithRetryAsync(
        ISmsSender sender,
        SmsMessage message,
        string providerName,
        CancellationToken ct)
    {
        var totalAttempts = _options.RetryCount + 1;
        Exception? lastEx = null;

        for (int attempt = 1; attempt <= totalAttempts; attempt++)
        {
            try
            {
                _logger.LogInformation(
                    "SMS attempt {Attempt}/{Total} via {Provider} to {Recipients}",
                    attempt, totalAttempts, providerName, message.To);

                await sender.SendAsync(message, ct);
                return;
            }
            catch (Exception ex) when (attempt < totalAttempts)
            {
                lastEx = ex;
                _logger.LogWarning(ex,
                    "SMS attempt {Attempt} failed via {Provider}. Retrying in {Delay}ms...",
                    attempt, providerName, _options.RetryDelayMs);
                await Task.Delay(_options.RetryDelayMs, ct);
            }
        }

        throw new SmsException(
            string.Join(", ", message.To),
            providerName,
            $"Failed to send SMS via {providerName} after {totalAttempts} attempt(s).",
            lastEx);
    }

    private ISmsSender Resolve(string providerName) => providerName switch
    {
        "Corvass" => _corvass,
        "Twilio"  => _twilio,
        _         => _console,
    };
}

/// <summary>Thrown when an SMS send fails after all retry attempts.</summary>
public class SmsException : Exception
{
    public string PhoneNumber  { get; }
    public string ProviderName { get; }

    public SmsException(string phoneNumber, string providerName, string message, Exception? inner = null)
        : base(message, inner)
    {
        PhoneNumber  = phoneNumber;
        ProviderName = providerName;
    }
}
