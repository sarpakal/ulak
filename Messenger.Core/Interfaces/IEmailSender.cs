// File name: IEmailSender.cs

using System.Threading.Tasks;
using System.Threading;
namespace Messenger.Core.Interfaces;

public interface IEmailSender
{
    Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default);
}
