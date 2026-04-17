using System;
using Messenger.Core.Options;
using Microsoft.Extensions.Options;

namespace Messenger.Infrastructure.Senders;

public class PushNotificationSender : IPushNotificationSender
{
    private readonly FcmNotificationOptions _options;
    public PushNotificationSender(FcmNotificationOptions options)
    {
        _options = options;
    }
    public Task SendAsync(PushMessage message, CancellationToken ct = default)
    {
        // Implement FCM push notification sending logic here
        Console.WriteLine($"Sending Push Notification to {message.To} via FCM at {_options.FcmEndpoint}"); // message.To = message.DeviceToken !
        return Task.CompletedTask;
    }
}
