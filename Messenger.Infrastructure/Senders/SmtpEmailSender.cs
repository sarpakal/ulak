using MailKit.Net.Smtp;
using Messenger.Core.DTOs;
using Messenger.Core.Interfaces;
using Messenger.Core.Options;
using Microsoft.Extensions.Options;
using MimeKit;
using System.Threading.Tasks;

namespace Messenger.Infrastructure.Senders;

public class SmtpEmailSender : IEmailSender
{
    private readonly EmailOptions _options;

    public SmtpEmailSender(IOptions<EmailOptions> options)
    {
        _options = options.Value;
    }

    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        var email = new MimeMessage();
        email.From.Add(MailboxAddress.Parse(_options.SenderEmail));
        email.To.AddRange(message.To.Select(t => MailboxAddress.Parse(t)));
        if (message.Cc != null) email.Cc.AddRange(message.Cc.Select(c => MailboxAddress.Parse(c)));
        if (message.Bcc != null) email.Bcc.AddRange(message.Bcc.Select(b => MailboxAddress.Parse(b)));
        email.Subject = message.Subject;
        email.Body = new TextPart("plain") { Text = message.Body };

        using var smtp = new SmtpClient();
        await smtp.ConnectAsync(_options.SmtpHost, _options.SmtpPort, MailKit.Security.SecureSocketOptions.StartTls, cancellationToken);
        await smtp.AuthenticateAsync(_options.SenderEmail, _options.SenderPassword, cancellationToken);
        await smtp.SendAsync(email, cancellationToken: cancellationToken);
        await smtp.DisconnectAsync(true, cancellationToken);
    }


    /*
        private readonly string _host;
        private readonly int _port;
        private readonly string _username;
        private readonly string _password;

        public SmtpEmailSender(string host, int port, string username, string password)
        {
            _host = host;
            _port = port;
            _username = username;
            _password = password;
        }
    
    public async Task SendAsync(EmailMessage message)
    {
        var email = new MimeMessage();
        email.From.Add(MailboxAddress.Parse(_username));
        email.To.Add(MailboxAddress.Parse(message.To));
        if (message.Cc != null) email.Cc.AddRange(message.Cc.Select(c => MailboxAddress.Parse(c)));
        if (message.Bcc != null) email.Bcc.AddRange(message.Bcc.Select(b => MailboxAddress.Parse(b)));
        email.Subject = message.Subject;
        email.Body = new TextPart("plain") { Text = message.Body };

        using var smtp = new SmtpClient();
        await smtp.ConnectAsync(_host, _port, MailKit.Security.SecureSocketOptions.StartTls);
        await smtp.AuthenticateAsync(_username, _password);
        await smtp.SendAsync(email);
        await smtp.DisconnectAsync(true);
    }
    */
}
