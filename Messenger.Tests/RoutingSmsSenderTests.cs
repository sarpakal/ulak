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
    // Default routing table: +90 → Corvass, +1 → Twilio. A +44 number matches neither.
    private static RoutingSmsSender BuildSender(
        bool allowConsoleFallback,
        HttpClient? corvassHttp = null,
        int retryCount = 0,
        Dictionary<string, string>? providerPrefixes = null)
    {
        var smsOptions = Options.Create(new SmsOptions
        {
            RetryCount = retryCount,
            RetryDelayMs = 0,
            AllowConsoleFallback = allowConsoleFallback,
            ProviderPrefixes = providerPrefixes ?? new Dictionary<string, string>
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

    // Counts calls, records each request body, and always returns the given status so
    // CorvassSmsSender throws on every attempt.
    private sealed class CountingHandler(HttpStatusCode status) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        public List<string> Bodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            if (request.Content is not null)
                Bodies.Add(await request.Content.ReadAsStringAsync(cancellationToken));

            return new HttpResponseMessage(status)
            {
                Content = new StringContent("provider failure"),
            };
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

    [Fact]
    public async Task SendAsync_MixedPrefixBatch_GroupsRecipientsByProvider()
    {
        // Batch has one +90 (Corvass) and one +1 (Twilio) recipient. GroupBy yields the
        // Corvass group first (its recipient appears first), so it dispatches first; with
        // Corvass stubbed to fail, SendAsync throws before the Twilio group is reached —
        // which is what lets us test this at all, since TwilioSmsSender can't be exercised
        // in a unit test under the concrete-type design (LESSONS #2). We assert Corvass
        // received ONLY its +90 recipient, proving the batch was split by provider.
        var handler = new CountingHandler(HttpStatusCode.InternalServerError);
        var sender = BuildSender(
            allowConsoleFallback: false,
            corvassHttp: new HttpClient(handler),
            retryCount: 0);
        var message = new SmsMessage(["+905551112233", "+15551112222"], "hello");

        var act = () => sender.SendAsync(message);

        var ex = (await act.Should().ThrowAsync<SmsException>()).Which;
        ex.ProviderName.Should().Be("Corvass");
        ex.PhoneNumber.Should().Contain("+905551112233").And.NotContain("+15551112222");
        handler.Calls.Should().Be(1); // retryCount 0 → single attempt for the Corvass group
        // Match on digits only — System.Text.Json escapes the leading '+' to its
        // unicode form in the JSON body, so a literal "+" substring won't be found.
        handler.Bodies.Should().ContainSingle()
            .Which.Should().Contain("905551112233").And.NotContain("15551112222");
    }

    [Fact]
    public async Task SendAsync_PrefixMapsToUnregisteredProvider_ThrowsSmsException()
    {
        // A prefix mapped to a provider name with no registered sender is a config bug,
        // not a routing miss — it must throw, never silently fall through to console
        // (even with the console fallback enabled, since only unmatched prefixes are gated).
        var sender = BuildSender(
            allowConsoleFallback: true,
            providerPrefixes: new Dictionary<string, string> { ["+44"] = "Vodafone" });
        var message = new SmsMessage(["+441234567890"], "hello");

        var act = () => sender.SendAsync(message);

        var ex = (await act.Should().ThrowAsync<SmsException>()).Which;
        ex.ProviderName.Should().Be("Vodafone");
        ex.Message.Should().Contain("unknown provider");
    }
}
