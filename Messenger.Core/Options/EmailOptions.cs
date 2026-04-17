namespace Messenger.Core.Options;

public record EmailOptions
{
    public required string SmtpHost { get; init; }
    public int SmtpPort { get; init; }
    public required string SenderEmail { get; init; }
    public required string SenderPassword { get; init; }
}
