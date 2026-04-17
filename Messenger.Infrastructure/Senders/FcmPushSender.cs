using Messenger.Core.Options;
using Microsoft.Extensions.Options;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Messenger.Infrastructure.Senders;

public class FcmPushSender : IPushNotificationSender
{
    private readonly FcmNotificationOptions _options;
    private readonly HttpClient _http;

    public FcmPushSender(HttpClient httpClient, IOptions<FcmNotificationOptions> options)
    {
        _http = httpClient;
        _options = options.Value;
    }

    public async Task SendAsync(PushMessage message, CancellationToken cancellationToken = default)
    {
        _http.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("key", "=" + _options.ServerKey);

        var payload = new
        {
            to = message.To,
            notification = new
            {
                title = message.Title,
                body = message.Body
            }
        };

        var response = await _http.PostAsJsonAsync(_options.FcmEndpoint, payload, cancellationToken);
        response.EnsureSuccessStatusCode();
    }
}
/*
    Token = "device-token",
    Notification = new Notification
    {
        Title = "Sarpwear Drop",
        Body = "New seasonal capsule is live!"
    }
*/

