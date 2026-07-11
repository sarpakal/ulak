using System.Net;
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
    private static RoutingSmsSender BuildSender(
        bool allowConsoleFallback,
        HttpClient? corvassHttp = null,
        int retryCount = 0)
    {
        var smsOptions = Options.Create(new SmsOptions
        {
            RetryCount = retryCount,
            RetryDelayMs = 0,
            AllowConsoleFallback = allowConsoleFallback,
            ProviderPrefixes = new Dictionary<string, string>
            {
                ["+90"] = "Corvass",
                ["+1"]  = "Twilio",
            },
        });

        // Concrete senders are required by RoutingSmsSender's constructor (LESSONS #2 debt).
        // Tests that only exercise routing never invoke them; the retry test injects a
        // stubbed HttpClient into Corvass so its SendAsync fails deterministically.
        var corvass = new CorvassSmsSender(
            corvassHttp ?? new HttpClient(),
            Options.Create(new CorvassOptions { SmsUrl = "https://corvass.invalid/json" }),
            NullLogger<CorvassSmsSender>.Instance);

        var twilio = new TwilioSmsSender(
            Options.Create(new TwilioOptions { AccountSid = "AC_test_sid", AuthToken = "test_token" }),
            NullLogger<TwilioSmsSender>.Instance);

        var console = new ConsoleSmsService(NullLogger<ConsoleSmsService>.Instance);

        return new RoutingSmsSender(
            corvass, twilio, console, smsOptions, NullLogger<RoutingSmsSender>.Instance);
    }

    // Counts calls and always returns the given status so CorvassSmsSender throws each attempt.
    private sealed class CountingHandler(HttpStatusCode status) : HttpMessageHandler
    {
        public int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent("provider failure"),
            });
        }
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

    [Fact]
    public async Task SendAsync_ProviderFailsEveryAttempt_ThrowsSmsExceptionAfterRetryExhaustion()
    {
        // Corvass returns 500 on every call → CorvassSmsSender throws each attempt.
        // With RetryCount = 2 the router should try 3 times, then surface SmsException.
        var handler = new CountingHandler(HttpStatusCode.InternalServerError);
        var sender = BuildSender(
            allowConsoleFallback: false,
            corvassHttp: new HttpClient(handler),
            retryCount: 2);
        var message = new SmsMessage(["+905551112233"], "hello");

        var act = () => sender.SendAsync(message);

        var ex = (await act.Should().ThrowAsync<SmsException>()).Which;
        ex.ProviderName.Should().Be("Corvass");
        handler.Calls.Should().Be(3); // RetryCount (2) + 1 initial attempt
    }
}
