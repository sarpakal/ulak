using AuthApi.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Messenger.Infrastructure.Senders;

/// <summary>
/// Sends OTP SMS via Twilio (US lines, +1).
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

    public async Task<bool> SendOtpAsync(
        string phoneNumber,
        string otpCode,
        CancellationToken ct = default)
    {
        _logger.LogWarning(
            "TwilioSmsSender: Twilio package not yet installed. " +
            "OTP for {Phone}: {Otp}", phoneNumber, otpCode);

        // ── Activate when Twilio NuGet is installed ──────────────────────────
        //
        // var message = await MessageResource.CreateAsync(
        //     body: $"Your verification code is {otpCode}. Valid for 2 minutes.",
        //     from: new Twilio.Types.PhoneNumber(_options.FromNumber),
        //     to:   new Twilio.Types.PhoneNumber(phoneNumber));
        //
        // return message.ErrorCode == null;
        // ─────────────────────────────────────────────────────────────────────

        await Task.CompletedTask;
        return true; // stub always succeeds
    }

    public Task<bool> SendAsync(string phoneNumber, string otpCode, CancellationToken ct = default)
    {
        // Alias to SendOtpAsync for compatibility
        return SendOtpAsync(phoneNumber, otpCode, ct);
    }
}
