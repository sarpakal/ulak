
using System.Threading;
using System.Threading.Tasks;

namespace Messenger.Core.Interfaces;

public interface IWhatsAppMessageSender
{
    Task SendAsync(WhatsAppMessage message, CancellationToken cancellationToken = default);
}
