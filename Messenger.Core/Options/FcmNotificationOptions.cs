
namespace Messenger.Core.Options;

public record FcmNotificationOptions
{
    public required string ServerKey { get; init; }
    public required string FcmEndpoint { get; init; }
}
