using System.Text.Json;
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
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        // Transport-level failure (network / 5xx).
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"Corvass SMS send failed: {response.StatusCode} - {body}");

        // Corvass returns HTTP 200 even when it REJECTS the request (bad auth, invalid number,
        // etc.) — the real result is Response.code in the body (0 = success). It also
        // inconsistently cases the field (`code` on success, `Code` on failure), so parse
        // case-insensitively. Without this, a rejected send looked like success — which masked
        // a ~2-week production outage (see solution LESSONS).
        CorvassResponse? result;
        try
        {
            result = JsonSerializer.Deserialize<CorvassResponse>(body, CaseInsensitiveJson);
        }
        catch (JsonException ex)
        {
            throw new HttpRequestException($"Corvass returned an unparseable response: {body}", ex);
        }

        var code = result?.Response?.Code;
        if (code != 0)
            throw new HttpRequestException(
                $"Corvass rejected the SMS (code {code?.ToString() ?? "none"}): " +
                $"{result?.Response?.Description ?? body}");

        _logger.LogInformation(
            "Corvass accepted SMS for {Recipients}, PacketId={PacketId}", message.To, result?.PacketId);
    }

    private static readonly JsonSerializerOptions CaseInsensitiveJson = new() { PropertyNameCaseInsensitive = true };

    private sealed record CorvassResponse(CorvassResponseBody? Response, long? PacketId);
    private sealed record CorvassResponseBody(int? Code, string? Description);
}
