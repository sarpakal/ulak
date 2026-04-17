using Messenger.Core.Options;
using Microsoft.Extensions.Options;
using System.Security.Cryptography;

namespace Messenger.Infrastructure.Senders;

public interface IOtpService
{
    string Generate();
    bool IsExpired(DateTime? expiry);
}

public class OtpService(IOptions<OtpOptions> options) : IOtpService
{
    private readonly OtpOptions _options = options.Value;

    public int ExpirySeconds => _options.ExpirySeconds;
    public int MaxAttempts => _options.MaxAttempts;

    public string Generate()
    {
        var value = RandomNumberGenerator.GetInt32(0, 1_000_000);
        return value.ToString("D6");
    }

    public bool IsExpired(DateTime? expiry) =>
        expiry == null || expiry < DateTime.UtcNow;
}