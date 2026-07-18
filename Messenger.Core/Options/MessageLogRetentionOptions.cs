namespace Messenger.Core.Options;

/// <summary>
/// Retention policy for the <c>MessageLogs</c> table. Every send persists the recipient
/// (phone/email — PII) and the payload (message body), so logs must not accumulate forever
/// (KVKK/GDPR — see SECURITY.md). Bound from the "MessageLogRetention" config section.
/// </summary>
public record MessageLogRetentionOptions
{
    public const string SectionName = "MessageLogRetention";

    /// <summary>Master switch. When <c>false</c>, the background cleanup job does not run.</summary>
    public bool Enabled { get; init; } = true;

    /// <summary>How often the cleanup runs, in hours. Clamped to a minimum of 1.</summary>
    public int RunIntervalHours { get; init; } = 24;

    /// <summary>
    /// Rows whose <c>CreatedAt</c> is older than this many days are deleted. Must be
    /// &gt;= 1: a value &lt; 1 is treated as misconfiguration and the run is skipped
    /// (fail-safe — never delete the whole table because someone set 0).
    /// </summary>
    public int RetentionDays { get; init; } = 90;
}
