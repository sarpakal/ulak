using Messenger.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Messenger.Infrastructure.Senders;

/// <summary>
/// Sends SMS via the Corvass API (Turkish mobile lines, +90).
/// </summary>
public class CorvassSmsSender : Messenger.Core.Interfaces.ISmsSender
{
    private readonly HttpClient _http;
    private readonly CorvassOptions _options;
    private readonly ILogger<CorvassSmsSender> _logger;

    public CorvassSmsSender(
        HttpClient httpClient,
        IOptions<CorvassOptions> options,
        ILogger<CorvassSmsSender> logger)
    {
        _http = httpClient;
        _options = options.Value;
        _logger = logger;
    }

    public async Task SendAsync(SmsMessage message, CancellationToken cancellationToken = default)
    {
        var payload = new
        {
            Authentication = new
            {
                apikey = _options.ApiKey,
                apisecret = _options.ApiSecret
            },
            msisdnArray = message.To,
            message = message.Text,
            originator = _options.Originator,
            senddate = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"),
            tags = new[] { "customer", "crm" },
            description = "CRM",
            messageType = _options.MessageType,
            recipientType = _options.RecipientType
        };

        var response = await _http.PostAsJsonAsync(_options.SmsUrl, payload, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new HttpRequestException($"Corvass SMS send failed: {response.StatusCode} - {error}");
        }
    }
}
