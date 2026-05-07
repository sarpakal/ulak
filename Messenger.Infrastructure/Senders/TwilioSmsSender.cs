using AuthApi.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Messenger.Infrastructure.Senders;

/// <summary>
/// Sends SMS via Twilio (US/CA lines, +1).
///
/// To activate:
///   dotnet add package Twilio
///   Uncomment the implementation below and remove the stub body.
/// </summary>
public class TwilioSmsSender : ISmsService
{
    private readonly TwilioOptions _options;
    private readonly ILogger<TwilioSmsSender> _logger;

    public TwilioSmsSender(
        IOptions<TwilioOptions> options,
        ILogger<TwilioSmsSender> logger)
    {
        _options = options.Value;
        _logger  = logger;

        // Uncomment when Twilio package is installed:
        // TwilioClient.Init(_options.AccountSid, _options.AuthToken);
    }

    public async Task<bool> SendAsync(string phoneNumber, string text, CancellationToken ct = default)
    {
        _logger.LogWarning(
            "TwilioSmsSender: Twilio package not yet installed. " +
            "SMS for {Phone}: {Text}", phoneNumber, text);

        // ── Activate when Twilio NuGet is installed ──────────────────────────
        //
        // var message = await MessageResource.CreateAsync(
        //     body: text,
        //     from: new Twilio.Types.PhoneNumber(_options.FromNumber),
        //     to:   new Twilio.Types.PhoneNumber(phoneNumber));
        //
        // return message.ErrorCode == null;
        // ─────────────────────────────────────────────────────────────────────

        await Task.CompletedTask;
        return true; // stub always succeeds
    }
}
