
namespace Messenger.Core.Options;

public record WhatsAppOptions
{
    public required string ApiUrl { get; init; }
    public required string ApiKey { get; init; }
    public required string SenderNumber { get; init; }
}
