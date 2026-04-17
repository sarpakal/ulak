namespace AuthApi.Models;

public class SmsOptions
{
    public const string SectionName = "Sms";

    /// <summary>How many times to retry a failed send (1 = one retry = 2 total attempts).</summary>
    public int RetryCount { get; init; } = 1;

    /// <summary>Milliseconds to wait between attempts.</summary>
    public int RetryDelayMs { get; init; } = 1000;

    /// <summary>
    /// Maps E.164 phone prefix → provider name.
    /// e.g. { "+90": "Corvass", "+1": "Twilio" }
    /// Matched longest-prefix-first so +901 won't accidentally match +90.
    /// </summary>
    public Dictionary<string, string> ProviderPrefixes { get; init; } = new();

    /// <summary>Resolves the provider name for a given E.164 phone number.</summary>
    public string? ResolveProvider(string phoneNumber)
    {
        // Sort descending by key length so more specific prefixes win
        var match = ProviderPrefixes
            .OrderByDescending(kv => kv.Key.Length)
            .FirstOrDefault(kv => phoneNumber.StartsWith(kv.Key, StringComparison.Ordinal));

        return match.Value; // null if no prefix matched
    }
}
