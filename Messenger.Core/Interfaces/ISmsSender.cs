using System.Threading.Tasks;
using System.Threading;

namespace Messenger.Core.Interfaces;

public interface ISmsSender
{
    Task SendAsync(SmsMessage message, CancellationToken cancellationToken = default);
}
