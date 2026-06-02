using Messenger.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Twilio;
using Twilio.Rest.Api.V2010.Account;

namespace Messenger.Infrastructure.Senders;

public class TwilioSmsSender : ISmsSender
{
    private readonly TwilioOptions _options;
    private readonly ILogger<TwilioSmsSender> _logger;

    public TwilioSmsSender(
        IOptions<TwilioOptions> options,
        ILogger<TwilioSmsSender> logger)
    {
        _options = options.Value;
        _logger  = logger;
        TwilioClient.Init(_options.AccountSid, _options.AuthToken);
    }

    public async Task SendAsync(SmsMessage message, CancellationToken cancellationToken = default)
    {
        foreach (var number in message.To)
            await SendSingleAsync(number, message.Text, cancellationToken);
    }

    private async Task SendSingleAsync(string phoneNumber, string text, CancellationToken ct)
    {
        _logger.LogInformation("Sending SMS via Twilio to {Phone}", phoneNumber);

        var msg = await MessageResource.CreateAsync(
            body: text,
            from: new Twilio.Types.PhoneNumber(_options.FromNumber),
            to:   new Twilio.Types.PhoneNumber(phoneNumber));

        if (msg.ErrorCode != null)
            throw new HttpRequestException($"Twilio error {msg.ErrorCode}: {msg.ErrorMessage}");

        _logger.LogInformation("Twilio SMS sent to {Phone}, SID={Sid}", phoneNumber, msg.Sid);
    }
}
