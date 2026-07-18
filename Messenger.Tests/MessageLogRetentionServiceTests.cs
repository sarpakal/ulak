using FluentAssertions;
using Messenger.Core.Options;
using Messenger.Infrastructure.Data;
using Messenger.Infrastructure.Retention;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Messenger.Tests;

/// <summary>
/// Exercises the MessageLogs retention cleanup against a real PostgreSQL container:
/// rows older than the window are deleted, recent rows survive, and a non-positive
/// window is treated as misconfiguration (skips — never wipes the table).
/// Recipients are stamped per test so the shared collection DB stays isolated.
/// </summary>
[Collection("ulak-db")]
public sealed class MessageLogRetentionServiceTests(PostgresFixture fx)
{
    private static MessageLogRetentionService CreateService(MessengerDbContext db, int retentionDays) =>
        new(db,
            Options.Create(new MessageLogRetentionOptions { RetentionDays = retentionDays }),
            NullLogger<MessageLogRetentionService>.Instance);

    [Fact]
    public async Task RunAsync_DeletesRowsOlderThanRetention_KeepsRecent()
    {
        var stamp = Guid.NewGuid().ToString("N")[..8];
        var oldRecipient = $"+90-old-{stamp}";
        var newRecipient = $"+90-new-{stamp}";

        await using (var seed = fx.NewDbContext())
        {
            seed.MessageLogs.Add(new MessageLog
            {
                Channel = "SMS", Recipient = oldRecipient, Status = "Sent",
                CreatedAt = DateTimeOffset.UtcNow.AddDays(-200),
            });
            seed.MessageLogs.Add(new MessageLog
            {
                Channel = "SMS", Recipient = newRecipient, Status = "Sent",
                CreatedAt = DateTimeOffset.UtcNow.AddDays(-1),
            });
            await seed.SaveChangesAsync();
        }

        await using (var db = fx.NewDbContext())
        {
            var deleted = await CreateService(db, 90).RunAsync();
            deleted.Should().BeGreaterThanOrEqualTo(1); // at least our aged row
        }

        await using (var verify = fx.NewDbContext())
        {
            (await verify.MessageLogs.AnyAsync(l => l.Recipient == oldRecipient)).Should().BeFalse();
            (await verify.MessageLogs.AnyAsync(l => l.Recipient == newRecipient)).Should().BeTrue();
        }
    }

    [Fact]
    public async Task RunAsync_RetentionDaysBelowOne_SkipsAndDeletesNothing()
    {
        var stamp = Guid.NewGuid().ToString("N")[..8];
        var ancient = $"+90-ancient-{stamp}";

        await using (var seed = fx.NewDbContext())
        {
            seed.MessageLogs.Add(new MessageLog
            {
                Channel = "SMS", Recipient = ancient, Status = "Sent",
                CreatedAt = DateTimeOffset.UtcNow.AddDays(-500),
            });
            await seed.SaveChangesAsync();
        }

        try
        {
            await using (var db = fx.NewDbContext())
            {
                var deleted = await CreateService(db, 0).RunAsync();
                deleted.Should().Be(0); // fail-safe: never delete when the window is nonsensical
            }

            await using (var verify = fx.NewDbContext())
            {
                (await verify.MessageLogs.AnyAsync(l => l.Recipient == ancient)).Should().BeTrue();
            }
        }
        finally
        {
            // Don't leave an aged row for a later real-retention run to sweep up.
            await using var cleanup = fx.NewDbContext();
            await cleanup.MessageLogs.Where(l => l.Recipient == ancient).ExecuteDeleteAsync();
        }
    }
}
