using Microsoft.Extensions.Logging;

namespace Messenger.Infrastructure.Senders;

/// <summary>
/// Development fallback — logs to console instead of sending a real SMS.
/// Used when no provider prefix matches or when running without credentials.
/// Never register this in production.
/// </summary>
public class ConsoleSmsService : ISmsService
{
    private readonly ILogger<ConsoleSmsService> _logger;

    public ConsoleSmsService(ILogger<ConsoleSmsService> logger) => _logger = logger;

    public Task<bool> SendAsync(string phoneNumber, string text, CancellationToken ct = default)
    {
        _logger.LogWarning(
            "DEV MODE — SMS for {Phone}: {Text}  ← do not log message content in production!",
            phoneNumber, text);

        return Task.FromResult(true);
    }
}
