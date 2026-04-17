
namespace Messenger.Core.DTOs;

public record EmailMessage(
    List<string> To,
    string Subject,
    string Body,
    List<string>? Cc = null,
    List<string>? Bcc = null
);
//    string[]? Cc = null,
//    string[]? Bcc = null
