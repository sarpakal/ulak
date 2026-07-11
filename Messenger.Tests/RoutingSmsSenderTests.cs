using FluentAssertions;
using Messenger.Core.DTOs;
using Messenger.Core.Models;
using Messenger.Infrastructure.Senders;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Messenger.Tests;

public class RoutingSmsSenderTests
{
    // Standard routing table: +90 → Corvass, +1 → Twilio. A +44 number matches neither.
    private static RoutingSmsSender BuildSender(bool allowConsoleFallback)
    {
        var smsOptions = Options.Create(new SmsOptions
        {
            RetryCount = 0,
            RetryDelayMs = 0,
            AllowConsoleFallback = allowConsoleFallback,
            ProviderPrefixes = new Dictionary<string, string>
            {
                ["+90"] = "Corvass",
                ["+1"]  = "Twilio",
            },
        });

        // Concrete senders are required by RoutingSmsSender's constructor (LESSONS #2 debt).
        // For unmatched-prefix routing they are never invoked — Resolve throws before dispatch —
        // so dummy credentials that construct without a network call are sufficient.
        var corvass = new CorvassSmsSender(
            new HttpClient(),
            Options.Create(new CorvassOptions()),
            NullLogger<CorvassSmsSender>.Instance);

        var twilio = new TwilioSmsSender(
            Options.Create(new TwilioOptions { AccountSid = "AC_test_sid", AuthToken = "test_token" }),
            NullLogger<TwilioSmsSender>.Instance);

        var console = new ConsoleSmsService(NullLogger<ConsoleSmsService>.Instance);

        return new RoutingSmsSender(
            corvass, twilio, console, smsOptions, NullLogger<RoutingSmsSender>.Instance);
    }

    [Fact]
    public async Task SendAsync_UnmatchedPrefix_FallbackDisabled_ThrowsSmsException()
    {
        var sender = BuildSender(allowConsoleFallback: false);
        var message = new SmsMessage(["+441234567890"], "hello");

        var act = () => sender.SendAsync(message);

        (await act.Should().ThrowAsync<SmsException>())
            .Which.ProviderName.Should().Be("none");
    }

    [Fact]
    public async Task SendAsync_UnmatchedPrefix_FallbackEnabled_DoesNotThrow()
    {
        // With the dev-only fallback flag on, an unmatched prefix routes to the console
        // sender instead of throwing — proving the throw is gated, not unconditional.
        var sender = BuildSender(allowConsoleFallback: true);
        var message = new SmsMessage(["+441234567890"], "hello");

        var act = () => sender.SendAsync(message);

        await act.Should().NotThrowAsync();
    }
}
