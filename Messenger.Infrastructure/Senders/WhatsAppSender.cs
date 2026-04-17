using Messenger.Core.Options;
using Microsoft.Extensions.Options;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using System.Threading;

namespace Messenger.Infrastructure.Senders;

public class WhatsappSender : IWhatsAppMessageSender
{
    private readonly WhatsAppOptions _options;
    private readonly HttpClient _http;

    public WhatsappSender(HttpClient httpClient, IOptions<WhatsAppOptions> options)
    {
        _http = httpClient;
        _options = options.Value;
    }

    public async Task SendAsync(WhatsAppMessage message, CancellationToken cancellationToken = default)
    {
        _http.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _options.ApiKey);

        var payload = new
        {
            messaging_product = "whatsapp",
            to = message.To,
            type = "text",
            text = new { body = message.Text }
        };

        var response = await _http.PostAsJsonAsync(_options.ApiUrl, payload, cancellationToken);
        response.EnsureSuccessStatusCode();
    }
}

/*
    private readonly string _wabaUrl;
    private readonly string _accessToken;

    public WhatsappSender(string wabaUrl, string accessToken)
    {
        _wabaUrl = wabaUrl;
        _accessToken = accessToken;
    }
*/
