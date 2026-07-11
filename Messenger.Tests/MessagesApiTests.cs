using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Messenger.Core.DTOs;
using Messenger.Core.Exceptions;
using Messenger.Core.Interfaces;
using Messenger.Infrastructure.Senders;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Messenger.Tests;

/// <summary>
/// Exercises POST /api/messages/sms end-to-end against a real PostgreSQL container, asserting
/// the MessageLogs write-on-success and write-on-failure paths. The real SMS provider is
/// swapped for a fake ISmsSender so no external call is made — the send outcome is what drives
/// the log Status.
/// </summary>
[Collection("ulak-db")]
public sealed class MessagesApiTests(PostgresFixture fx)
{
    private WebApplicationFactory<Program> CreateApp(ISmsSender smsSender) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            // Point the app's DbContext at the test container.
            builder.UseSetting("ConnectionStrings:UlakConnection", fx.ConnectionString);

            // Replace the routing SMS sender (which would call Corvass/Twilio) with a fake.
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<ISmsSender>();
                services.AddScoped(_ => smsSender);
            });
        });

    [Fact]
    public async Task PostSms_SenderSucceeds_Returns200_AndWritesSentLog()
    {
        using var app = CreateApp(new SucceedingSmsSender());
        var client = app.CreateClient();
        const string to = "+905550000001"; // unique per test — the shared DB accumulates rows

        var resp = await client.PostAsJsonAsync("/api/messages/sms", new SmsMessage([to], "hi there"));

        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        await using var db = fx.NewDbContext();
        var log = await db.MessageLogs.SingleAsync(l => l.Recipient == to);
        log.Channel.Should().Be("SMS");
        log.Status.Should().Be("Sent");
        log.Payload.Should().Be("hi there");
        log.CorrelationId.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task PostSms_SenderThrows_Returns500_AndWritesFailedLog()
    {
        using var app = CreateApp(new ThrowingSmsSender());
        var client = app.CreateClient();
        const string to = "+905550000002";

        var resp = await client.PostAsJsonAsync("/api/messages/sms", new SmsMessage([to], "boom text"));

        // The controller rethrows on send failure → unhandled → 500, but the finally block
        // still records the attempt with Status = "Failed".
        resp.StatusCode.Should().Be(HttpStatusCode.InternalServerError);

        await using var db = fx.NewDbContext();
        var log = await db.MessageLogs.SingleAsync(l => l.Recipient == to);
        log.Channel.Should().Be("SMS");
        log.Status.Should().Be("Failed");
    }

    private sealed class SucceedingSmsSender : ISmsSender
    {
        public Task SendAsync(SmsMessage message, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    private sealed class ThrowingSmsSender : ISmsSender
    {
        public Task SendAsync(SmsMessage message, CancellationToken cancellationToken = default)
            => throw new SmsException(string.Join(", ", message.To), "Corvass", "simulated provider failure");
    }

    // ── Push: PushSendException → honest status codes + ErrorCode in MessageLogs ─────────

    private WebApplicationFactory<Program> CreatePushApp(IPushNotificationSender pushSender) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:UlakConnection", fx.ConnectionString);
            builder.ConfigureTestServices(services =>
            {
                // Replace the FCM typed client with a fake so no external call is made.
                services.RemoveAll<IPushNotificationSender>();
                services.AddScoped(_ => pushSender);
            });
        });

    [Fact]
    public async Task PostPush_Success_Returns200_AndLogsSent_WithNullErrorCode()
    {
        using var app = CreatePushApp(new FakePushSender(null));
        var client = app.CreateClient();
        const string to = "push-token-ok";

        var resp = await client.PostAsJsonAsync("/api/messages/push", new PushMessage(to, "title", "body"));

        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        await using var db = fx.NewDbContext();
        var log = await db.MessageLogs.SingleAsync(l => l.Recipient == to);
        log.Channel.Should().Be("Push");
        log.Status.Should().Be("Sent");
        log.ErrorCode.Should().BeNull();
    }

    [Fact]
    public async Task PostPush_DeadToken_Returns410_AndLogsErrorCode()
    {
        using var app = CreatePushApp(new FakePushSender(new PushSendException(
            PushSendFailureReason.InvalidToken, "FCM send failed (404 UNREGISTERED)",
            "UNREGISTERED", 404)));
        var client = app.CreateClient();
        const string to = "push-token-dead";

        var resp = await client.PostAsJsonAsync("/api/messages/push", new PushMessage(to, "title", "body"));

        resp.StatusCode.Should().Be(HttpStatusCode.Gone);
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        doc.RootElement.GetProperty("status").GetString().Should().Be("failed");
        doc.RootElement.GetProperty("errorCode").GetString().Should().Be("UNREGISTERED");

        await using var db = fx.NewDbContext();
        var log = await db.MessageLogs.SingleAsync(l => l.Recipient == to);
        log.Status.Should().Be("Failed");
        log.ErrorCode.Should().Be("UNREGISTERED");
    }

    [Fact]
    public async Task PostPush_QuotaExceeded_Returns429_WithRetryAfterHeader()
    {
        using var app = CreatePushApp(new FakePushSender(new PushSendException(
            PushSendFailureReason.QuotaExceeded, "FCM send failed (429 QUOTA_EXCEEDED)",
            "QUOTA_EXCEEDED", 429, TimeSpan.FromSeconds(30))));
        var client = app.CreateClient();
        const string to = "push-token-throttled";

        var resp = await client.PostAsJsonAsync("/api/messages/push", new PushMessage(to, "title", "body"));

        resp.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        resp.Headers.RetryAfter!.Delta.Should().Be(TimeSpan.FromSeconds(30));

        await using var db = fx.NewDbContext();
        var log = await db.MessageLogs.SingleAsync(l => l.Recipient == to);
        log.Status.Should().Be("Failed");
        log.ErrorCode.Should().Be("QUOTA_EXCEEDED");
    }

    [Fact]
    public async Task PostPush_ProviderError_Returns502()
    {
        using var app = CreatePushApp(new FakePushSender(new PushSendException(
            PushSendFailureReason.ProviderError, "FCM send failed (503 UNAVAILABLE)",
            "UNAVAILABLE", 503)));
        var client = app.CreateClient();
        const string to = "push-token-outage";

        var resp = await client.PostAsJsonAsync("/api/messages/push", new PushMessage(to, "title", "body"));

        resp.StatusCode.Should().Be(HttpStatusCode.BadGateway);

        await using var db = fx.NewDbContext();
        var log = await db.MessageLogs.SingleAsync(l => l.Recipient == to);
        log.Status.Should().Be("Failed");
        log.ErrorCode.Should().Be("UNAVAILABLE");
    }

    private sealed class FakePushSender(PushSendException? failure) : IPushNotificationSender
    {
        public Task SendAsync(PushMessage message, CancellationToken cancellationToken = default)
            => failure is null ? Task.CompletedTask : throw failure;
    }
}
