namespace Messenger.Core.Options;

public record CorvassApiOptions
{
    public required string SmsUrl { get; init; }
    public required string ApiKey { get; init; }
    public required string ApiSecret { get; init; }
}
