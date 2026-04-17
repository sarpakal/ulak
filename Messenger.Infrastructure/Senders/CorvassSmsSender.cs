using System.Globalization;
using System.Text;
using System.Text.Json;
using AuthApi.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Messenger.Infrastructure.Senders;

/// <summary>
/// Sends OTP SMS via the Corvass API (Turkish mobile lines, +90).
/// Adapted from CorvassOtpClient provided by Turquoise Auto Inc.
/// </summary>
public class CorvassSmsSender : ISmsService, Messenger.Core.Interfaces.ISmsSender
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
            msisdnArray = message.To, //msisdnArray = new[] { message.To },
            message = message.Text,
            originator = _options.Originator, // "AKAL YNT."
            senddate = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"),
            tags = new[] { "customer", "crm" },
            description = "CRM",
            messageType = _options.MessageType, // B = Business, R = Regular
            recipientType = _options.RecipientType // "TACIR" (commercial) or "BIREYSEL" (individual)
        };

        var response = await _http.PostAsJsonAsync(_options.SmsUrl, payload, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new HttpRequestException($"Corvass SMS send failed: {response.StatusCode} - {error}");
        }
    }

    public Task<bool> SendAsync(string phoneNumber, string otpCode, CancellationToken ct = default)
    {
        throw new NotImplementedException();
    }

    public Task<bool> SendOtpAsync(string phoneNumber, string otpCode, CancellationToken ct = default)
    {
        throw new NotImplementedException();
    }
}

    /*
    public async Task<bool> SendOtpAsync(
        string phoneNumber,
        string otpCode,
        CancellationToken ct = default)
    {
        var payload = new
        {
            Authentication = new
            {
                apikey    = _options.ApiKey,
                apisecret = _options.ApiSecret,
            },
            message      = $"{otpCode} tek seferlik giriş kodunuzdur.",
            msisdnArray  = new[] { phoneNumber },
            originator   = _options.Originator,
            senddate     = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
            tags         = new[] { "otp", "authentication" },
            description  = "OTP",
            messageType  = _options.MessageType,
            recipientType= _options.RecipientType,
        };

        var json = JsonSerializer.Serialize(payload);

        using var request = new HttpRequestMessage(HttpMethod.Post, _options.SmsUrl)
        {
            // Corvass expects text/plain content type
            Content = new StringContent(json, Encoding.UTF8, "text/plain")
        };

        _logger.LogInformation("Corvass: sending OTP to {Phone}", phoneNumber);

        var response = await _http.SendAsync(request, ct);
        var body     = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning(
                "Corvass: non-success status {Status} for {Phone}. Body: {Body}",
                (int)response.StatusCode, phoneNumber, body);
            return false;
        }

        _logger.LogInformation("Corvass: OTP sent to {Phone}. Response: {Body}", phoneNumber, body);
        return true;
    }
}

/*

public Task<bool> SendOtpSmsAsync(string to, string otpCode, int expiryMinutes)
{
    var message = $"Tek seferlik giriş kodunuz: {otpCode}. Kodun süresi {expiryMinutes} dakika sonra dolacak.";
    //var message = $"Your OTP code is {otpCode}. It will expire in {expiryMinutes} minutes.";
    return SendAsync(to, message).ContinueWith(t => t.IsCompletedSuccessfully);
}
public Task SendAsync(string to, string message, DateTime scheduledTime)
{
    // Note: Corvass API may not support scheduling directly.
    // You might need to implement scheduling logic in your application.
    throw new NotImplementedException("Scheduled sending is not implemented.");
}




     private readonly string _smsUrl;
private readonly string _apiKey;
private readonly string _apiSecret;

public CorvassSmsSender(string smsUrl, string apiKey, string apiSecret)
{
    _smsUrl = smsUrl;
    _apiKey = apiKey;
    _apiSecret = apiSecret;
}


   public async Task SendAsync(SmsMessage message)
{
    using var client = new HttpClient();

    var payload = new
    {
        Authentication = new
        {
            apikey = _apiKey,
            apisecret = _apiSecret
        },
        msisdnArray = new[] { message.To },
        message = message.Text,
        originator = "", // "AKAL YNT."
        senddate = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"),
        tags = new[] { "customer", "crm" },
        description = "",
        messageType = "B", // B = Business, R = Regular
        recipientType = "" // "TACIR" (commercial) or "BIREYSEL" (individual)
    };

    var response = await client.PostAsJsonAsync(_smsUrl, payload);
    response.EnsureSuccessStatusCode();

    if (!response.IsSuccessStatusCode)
    {
        var error = await response.Content.ReadAsStringAsync();
        throw new HttpRequestException($"Corvass SMS send failed: {response.StatusCode} - {error}");
    }
}


{
"Authentication": {
    "apikey": "{{apikey}}",
    "apisecret": "{{apisecret}}"
}
"message": "Mesaj metni",
"msisdnArray": ["53xxxxxxxx", "05xxxxxxxxx", "905xxxxxxxxx"],

"originator": "Corvass.NET",
"senddate": "2021-10-01 00:00:00",
"tags": ["kirtasiye", "indirim", "gönderim", "sms"],
"description": "",
“messageType”: “{B veya R}”,
“recipientType”: “{TACIR veya BIREYSEL}”
}     

 */

