
namespace Messenger.Core.DTOs;

public record PushMessage(
    string To,
    string Title,
    string Body
);
