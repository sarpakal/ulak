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
/// If no prefix matches, throws <see cref="SmsException"/> — unless Sms:AllowConsoleFallback
/// is true (dev only), in which case it logs to ConsoleSmsService. This fail-closed default
/// prevents silent message loss in production (an unmatched number would otherwise be dropped
/// with an HTTP 200).
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
        // Group recipients by provider so a mixed-prefix batch fans out correctly.
        // Keep null for unmatched prefixes — Resolve decides throw-vs-console, loudly.
        var groups = message.To.GroupBy(n => _options.ResolveProvider(n));

        foreach (var group in groups)
        {
            var providerName = group.Key;                 // null = no prefix matched
            var sender = Resolve(providerName, group);
            var batch = message with { To = group.ToList() };
            await SendWithRetryAsync(sender, batch, providerName ?? "Console", cancellationToken);
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
            catch (Exception ex)
            {
                // Catch every attempt (including the last) so the final failure is wrapped
                // in SmsException with provider context — not surfaced as the raw provider
                // exception. Only retry/delay while attempts remain.
                lastEx = ex;
                if (attempt < totalAttempts)
                {
                    _logger.LogWarning(ex,
                        "SMS attempt {Attempt} failed via {Provider}. Retrying in {Delay}ms...",
                        attempt, providerName, _options.RetryDelayMs);
                    await Task.Delay(_options.RetryDelayMs, ct);
                }
            }
        }

        throw new SmsException(
            string.Join(", ", message.To),
            providerName,
            $"Failed to send SMS via {providerName} after {totalAttempts} attempt(s).",
            lastEx);
    }

    private ISmsSender Resolve(string? providerName, IEnumerable<string> recipients) => providerName switch
    {
        "Corvass" => _corvass,
        "Twilio"  => _twilio,

        // No prefix matched: console only if explicitly allowed (dev), else fail loud.
        null when _options.AllowConsoleFallback => _console,
        null => throw new SmsException(
            string.Join(", ", recipients), "none",
            "No SMS provider matches the recipient prefix and console fallback is disabled. " +
            "Check Sms:ProviderPrefixes."),

        // A prefix mapped to a provider name we have no sender for = config bug, never console.
        _ => throw new SmsException(
            string.Join(", ", recipients), providerName,
            $"Sms:ProviderPrefixes maps to unknown provider '{providerName}' — no sender is registered."),
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
