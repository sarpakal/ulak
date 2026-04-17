
namespace Messenger.Core.DTOs;

public record SmsMessage(
    List<string> To,
    string Text
);
