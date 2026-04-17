using AuthApi.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Messenger.Infrastructure.Senders;

/// <summary>
/// Routes OTP SMS to the correct provider based on the phone number's E.164 prefix.
///
/// Routing table (from appsettings.json → Sms:ProviderPrefixes):
///   +90 → Corvass  (Turkish mobile lines)
///   +1  → Twilio   (US lines)
///
/// Retry policy: one retry after a configurable delay.
/// Falls back to ConsoleSmsService if no prefix matches (dev safety net).
/// Throws <see cref="SmsException"/> if all attempts fail.
/// </summary>
public class RoutingSmsSender : ISmsService
{
    private readonly IServiceProvider _services;
    private readonly SmsOptions       _options;
    private readonly ILogger<RoutingSmsSender> _logger;

    // Maps provider name (from config) → ISmsService implementation type
    private static readonly Dictionary<string, Type> ProviderMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Corvass"] = typeof(CorvassSmsSender),
        ["Twilio"]  = typeof(TwilioSmsSender),
        ["Console"] = typeof(ConsoleSmsService),
    };

    public RoutingSmsSender(
        IServiceProvider services,
        IOptions<SmsOptions> options,
        ILogger<RoutingSmsSender> logger)
    {
        _services = services;
        _options  = options.Value;
        _logger   = logger;
    }

    public Task<bool> SendAsync(string phoneNumber, string otpCode, CancellationToken ct = default)
        => SendOtpAsync(phoneNumber, otpCode, ct);

    public async Task<bool> SendOtpAsync(
        string phoneNumber,
        string otpCode,
        CancellationToken ct = default)
    {
        var providerName = _options.ResolveProvider(phoneNumber) ?? "Console";
        var sender       = Resolve(providerName, phoneNumber);

        var totalAttempts = _options.RetryCount + 1; // e.g. RetryCount=1 → 2 attempts
        Exception? lastEx = null;

        for (int attempt = 1; attempt <= totalAttempts; attempt++)
        {
            try
            {
                _logger.LogInformation(
                    "SMS attempt {Attempt}/{Total} via {Provider} to {Phone}",
                    attempt, totalAttempts, providerName, phoneNumber);

                var success = await sender.SendOtpAsync(phoneNumber, otpCode, ct);

                if (success)
                    return true;

                // Provider returned false (non-exception failure) — treat as retryable
                _logger.LogWarning(
                    "SMS attempt {Attempt} returned false via {Provider} for {Phone}",
                    attempt, providerName, phoneNumber);
            }
            catch (Exception ex) when (attempt < totalAttempts)
            {
                lastEx = ex;
                _logger.LogWarning(ex,
                    "SMS attempt {Attempt} failed via {Provider} for {Phone}. Retrying in {Delay}ms...",
                    attempt, providerName, phoneNumber, _options.RetryDelayMs);
            }

            if (attempt < totalAttempts)
                await Task.Delay(_options.RetryDelayMs, ct);
        }

        // All attempts exhausted
        throw new SmsException(
            phoneNumber,
            providerName,
            $"Failed to send OTP to {phoneNumber} via {providerName} after {totalAttempts} attempt(s).",
            lastEx);
    }

    private ISmsService Resolve(string providerName, string phoneNumber)
    {
        if (!ProviderMap.TryGetValue(providerName, out var type))
        {
            _logger.LogWarning(
                "Unknown SMS provider '{Provider}' for {Phone}. Falling back to Console.",
                providerName, phoneNumber);
            return _services.GetRequiredService<ConsoleSmsService>();
        }

        return (ISmsService)_services.GetRequiredService(type);
    }
}
