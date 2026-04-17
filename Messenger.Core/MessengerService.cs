// File name: MessengerService.cs
// File purpose: Central entry point for sending messages via different channels
// Description:
// This code defines a MessengerService class that acts as a facade for sending messages through various channels such as Email, SMS, WhatsApp, and Push Notifications. It uses interfaces to abstract the implementation details of each messaging service, allowing for easy extension and maintenance.
// Facade (Central Entry) 
// using interfaces for different messaging services
// Project target framework: .NET 10.0

namespace Messenger.Core;

public class MessengerService(
    IEmailSender emailSender,
    ISmsSender smsSender,
    IWhatsAppMessageSender whatsappSender,
    IPushNotificationSender pushSender)
{
    private readonly IEmailSender _emailSender = emailSender;
    private readonly ISmsSender _smsSender = smsSender;
    private readonly IWhatsAppMessageSender _whatsAppSender = whatsappSender;
    private readonly IPushNotificationSender _pushSender = pushSender;

    public Task SendEmailAsync(EmailMessage message, CancellationToken cancellationToken = default)
        => _emailSender.SendAsync(message, cancellationToken);

    public Task SendSmsAsync(SmsMessage message, CancellationToken cancellationToken = default)
        => _smsSender.SendAsync(message, cancellationToken);

    public Task SendWhatsappAsync(WhatsAppMessage message, CancellationToken cancellationToken = default)
        => _whatsAppSender.SendAsync(message, cancellationToken);

    public Task SendPushAsync(PushMessage message, CancellationToken cancellationToken = default)
        => _pushSender.SendAsync(message, cancellationToken);

}
