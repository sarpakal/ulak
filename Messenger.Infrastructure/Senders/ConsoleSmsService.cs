using Microsoft.Extensions.Logging;

namespace Messenger.Infrastructure.Senders;

/// <summary>
/// Development fallback — logs OTP to console instead of sending a real SMS.
/// Used when no provider prefix matches or when running without credentials.
/// Never register this in production.
/// </summary>
public class ConsoleSmsService : ISmsService
{
    private readonly ILogger<ConsoleSmsService> _logger;

    public ConsoleSmsService(ILogger<ConsoleSmsService> logger) => _logger = logger;

    public Task<bool> SendOtpAsync(
        string phoneNumber,
        string otpCode,
        CancellationToken ct = default)
    {
        _logger.LogWarning(
            "DEV MODE — OTP for {Phone}: {Otp}  ← do not log in production!",
            phoneNumber, otpCode);

        return Task.FromResult(true);
    }

        public Task<bool> SendAsync(string phoneNumber, string otpCode, CancellationToken ct = default)
        {
            // For development, treat SendAsync same as SendOtpAsync
            return SendOtpAsync(phoneNumber, otpCode, ct);
        }
}
