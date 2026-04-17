using System.Threading.Tasks;
using System.Threading;

namespace Messenger.Core.Interfaces;

public interface IPushNotificationSender
{
    Task SendAsync(PushMessage message, CancellationToken cancellationToken = default);
}
