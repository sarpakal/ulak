using Microsoft.Extensions.Logging;

namespace Messenger.Infrastructure.Senders;

/// <summary>
/// Development fallback — logs to console instead of sending a real SMS.
/// Used when no provider prefix matches or when running without credentials.
/// Never register this in production.
/// </summary>
public class ConsoleSmsService : ISmsSender
{
    private readonly ILogger<ConsoleSmsService> _logger;

    public ConsoleSmsService(ILogger<ConsoleSmsService> logger) => _logger = logger;

    public Task SendAsync(SmsMessage message, CancellationToken cancellationToken = default)
    {
        foreach (var number in message.To)
            _logger.LogWarning(
                "DEV MODE — SMS for {Phone}: {Text}  ← do not log message content in production!",
                number, message.Text);

        return Task.CompletedTask;
    }
}
