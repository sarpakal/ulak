using Messenger.Core.Options;
using Messenger.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Messenger.Infrastructure.Retention;

/// <summary>
/// Deletes <c>MessageLogs</c> rows older than the configured retention window.
/// Set-based (<c>ExecuteDeleteAsync</c>) — no entity materialisation.
/// </summary>
public interface IMessageLogRetentionService
{
    /// <summary>Runs one cleanup pass. Returns the number of rows deleted.</summary>
    Task<int> RunAsync(CancellationToken ct = default);
}

public class MessageLogRetentionService : IMessageLogRetentionService
{
    private readonly MessengerDbContext _db;
    private readonly MessageLogRetentionOptions _options;
    private readonly ILogger<MessageLogRetentionService> _logger;

    public MessageLogRetentionService(
        MessengerDbContext db,
        IOptions<MessageLogRetentionOptions> options,
        ILogger<MessageLogRetentionService> logger)
    {
        _db = db;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<int> RunAsync(CancellationToken ct = default)
    {
        // Fail-safe: a non-positive window would set the cutoff at/after "now" and wipe the
        // whole table. Treat it as misconfiguration and do nothing.
        if (_options.RetentionDays < 1)
        {
            _logger.LogWarning(
                "MessageLog retention skipped — RetentionDays={Days} is < 1 (would delete all rows). Fix MessageLogRetention config.",
                _options.RetentionDays);
            return 0;
        }

        var cutoff = DateTimeOffset.UtcNow.AddDays(-_options.RetentionDays);
        var startedAt = DateTimeOffset.UtcNow;

        var deleted = await _db.MessageLogs
            .Where(m => m.CreatedAt < cutoff)
            .ExecuteDeleteAsync(ct);

        var durationMs = (int)(DateTimeOffset.UtcNow - startedAt).TotalMilliseconds;
        _logger.LogInformation(
            "MessageLog retention: deleted {Count} row(s) older than {Cutoff:yyyy-MM-dd} ({Days}d) in {DurationMs}ms",
            deleted, cutoff, _options.RetentionDays, durationMs);

        return deleted;
    }
}
