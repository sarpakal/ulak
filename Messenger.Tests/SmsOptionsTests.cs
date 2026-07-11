using FluentAssertions;
using Messenger.Core.Models;
using Xunit;

namespace Messenger.Tests;

public class SmsOptionsTests
{
    private static SmsOptions WithPrefixes(params (string Prefix, string Provider)[] pairs)
    {
        var map = new Dictionary<string, string>();
        foreach (var (prefix, provider) in pairs)
            map[prefix] = provider;
        return new SmsOptions { ProviderPrefixes = map };
    }

    [Theory]
    [InlineData("+905551112233", "Corvass")]
    [InlineData("+15551112222", "Twilio")]
    public void ResolveProvider_MatchingPrefix_ReturnsProvider(string phone, string expected)
    {
        var options = WithPrefixes(("+90", "Corvass"), ("+1", "Twilio"));

        options.ResolveProvider(phone).Should().Be(expected);
    }

    [Fact]
    public void ResolveProvider_NoMatchingPrefix_ReturnsNull()
    {
        var options = WithPrefixes(("+90", "Corvass"), ("+1", "Twilio"));

        options.ResolveProvider("+441234567890").Should().BeNull();
    }

    [Fact]
    public void ResolveProvider_EmptyPrefixMap_ReturnsNull()
    {
        var options = new SmsOptions(); // ProviderPrefixes defaults to an empty dictionary

        options.ResolveProvider("+905551112233").Should().BeNull();
    }

    [Fact]
    public void ResolveProvider_LongerPrefixWins_RegardlessOfInsertionOrder()
    {
        // +1 (US) and +1204 (Manitoba, CA) both prefix-match a +1204… number.
        // The more specific (longer) prefix must win even though +1 was inserted first —
        // this is the longest-prefix-first guarantee in the XML doc.
        var options = WithPrefixes(("+1", "TwilioUS"), ("+1204", "TwilioCA"));

        options.ResolveProvider("+12045551234").Should().Be("TwilioCA");
    }
}
