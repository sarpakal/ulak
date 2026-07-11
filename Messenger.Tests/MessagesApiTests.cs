using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Messenger.Core.DTOs;
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
}
