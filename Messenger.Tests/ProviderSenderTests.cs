using System.Net;
using System.Text.Json;
using FluentAssertions;
using Messenger.Core.DTOs;
using Messenger.Core.Models;
using Messenger.Core.Options;
using Messenger.Infrastructure.Senders;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Messenger.Tests;

/// <summary>
/// Verifies the outbound HTTP request each provider sender builds (method, URL, auth header,
/// JSON body shape) by intercepting it with a capturing handler. Twilio is excluded — it sends
/// through the Twilio SDK's global client, not a testable HttpClient.
/// </summary>
public class ProviderSenderTests
{
    // Captures the request and its body, then returns 200 with the given body. Defaults to a
    // Corvass success payload (Response.code == 0) so the Corvass sender's body check passes;
    // WhatsApp/FCM ignore the body.
    private sealed class CapturingHandler(
        string responseJson = """{"Response":{"code":0,"description":"OK"},"PacketId":1}""") : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }
        public string Body { get; private set; } = "";
        public System.Net.Http.Headers.AuthenticationHeaderValue? Authorization { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Request = request;
            Authorization = request.Headers.Authorization;
            if (request.Content is not null)
                Body = await request.Content.ReadAsStringAsync(cancellationToken);

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseJson),
            };
        }
    }

    // Case-insensitive property lookup — System.Net.Http.Json serializes with web (camelCase)
    // defaults, so we don't want the assertions coupled to the exact casing policy.
    private static JsonElement Prop(JsonElement element, string name)
    {
        foreach (var property in element.EnumerateObject())
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
                return property.Value;

        throw new Xunit.Sdk.XunitException($"Property '{name}' not found in: {element.GetRawText()}");
    }

    [Fact]
    public async Task Corvass_BuildsExpectedRequest()
    {
        var handler = new CapturingHandler();
        var options = Options.Create(new CorvassOptions
        {
            SmsUrl        = "https://corvass.test/json",
            ApiKey        = "key123",
            ApiSecret     = "secret456",
            Originator    = "AKAL YNT.",
            MessageType   = "B",
            RecipientType = "BIREYSEL",
        });
        var sender = new CorvassSmsSender(new HttpClient(handler), options, NullLogger<CorvassSmsSender>.Instance);

        await sender.SendAsync(new SmsMessage(["+905551112233"], "hello world"));

        handler.Request!.Method.Should().Be(HttpMethod.Post);
        handler.Request.RequestUri!.ToString().Should().Be("https://corvass.test/json");
        handler.Authorization.Should().BeNull(); // Corvass auth is in the body, not a header

        using var doc = JsonDocument.Parse(handler.Body);
        var root = doc.RootElement;
        Prop(Prop(root, "authentication"), "apikey").GetString().Should().Be("key123");
        Prop(Prop(root, "authentication"), "apisecret").GetString().Should().Be("secret456");
        Prop(root, "msisdnArray")[0].GetString().Should().Be("+905551112233");
        Prop(root, "message").GetString().Should().Be("hello world");
        Prop(root, "originator").GetString().Should().Be("AKAL YNT.");
        Prop(root, "messageType").GetString().Should().Be("B");
        Prop(root, "recipientType").GetString().Should().Be("BIREYSEL");
    }

    [Fact]
    public async Task Corvass_NonZeroResponseCode_Throws()
    {
        // Corvass returns HTTP 200 even on rejection — the failure signal is Response.Code != 0,
        // and it cases the field differently (uppercase `Code`) than on success. The sender must
        // still catch it (case-insensitive), or a rejected send is silently reported as success.
        var handler = new CapturingHandler(
            """{"Response":{"Code":9999,"Description":"Hatalı / geçersiz authentication bilgisi"}}""");
        var sender = new CorvassSmsSender(
            new HttpClient(handler),
            Options.Create(new CorvassOptions { SmsUrl = "https://corvass.test/json" }),
            NullLogger<CorvassSmsSender>.Instance);

        var act = () => sender.SendAsync(new SmsMessage(["+905551112233"], "hi"));

        (await act.Should().ThrowAsync<HttpRequestException>())
            .Which.Message.Should().Contain("9999");
    }

    [Fact]
    public async Task WhatsApp_BuildsExpectedRequest()
    {
        var handler = new CapturingHandler();
        var options = Options.Create(new WhatsAppOptions
        {
            ApiUrl       = "https://graph.facebook.com/v19.0/PHONE_ID/messages",
            ApiKey       = "waba-token",
            SenderNumber = "+905550000000",
        });
        var sender = new WhatsappSender(new HttpClient(handler), options);

        await sender.SendAsync(new WhatsAppMessage("+905551112233", "hi via whatsapp"));

        handler.Request!.Method.Should().Be(HttpMethod.Post);
        handler.Request.RequestUri!.ToString().Should().Be("https://graph.facebook.com/v19.0/PHONE_ID/messages");
        handler.Authorization!.Scheme.Should().Be("Bearer");
        handler.Authorization.Parameter.Should().Be("waba-token");

        using var doc = JsonDocument.Parse(handler.Body);
        var root = doc.RootElement;
        Prop(root, "messaging_product").GetString().Should().Be("whatsapp");
        Prop(root, "to").GetString().Should().Be("+905551112233");
        Prop(root, "type").GetString().Should().Be("text");
        Prop(Prop(root, "text"), "body").GetString().Should().Be("hi via whatsapp");
    }

    [Fact]
    public async Task Fcm_BuildsExpectedV1Request()
    {
        var handler = new CapturingHandler();
        var options = Options.Create(new FcmNotificationOptions
        {
            BaseUrl   = "https://fcm.googleapis.com",
            ProjectId = "my-project",
            // CredentialsPath unused — the token provider is faked below.
        });
        var sender = new FcmPushSender(new HttpClient(handler), options, new FakeFcmTokenProvider("oauth-token-xyz"));

        await sender.SendAsync(new PushMessage("device-token", "Title Here", "Body here"));

        handler.Request!.Method.Should().Be(HttpMethod.Post);
        handler.Request.RequestUri!.ToString()
            .Should().Be("https://fcm.googleapis.com/v1/projects/my-project/messages:send");
        // HTTP v1: OAuth2 bearer, not the legacy server key.
        handler.Authorization!.Scheme.Should().Be("Bearer");
        handler.Authorization.Parameter.Should().Be("oauth-token-xyz");

        // v1 envelope: { "message": { "token": ..., "notification": { "title", "body" } } }
        using var doc = JsonDocument.Parse(handler.Body);
        var msg = Prop(doc.RootElement, "message");
        Prop(msg, "token").GetString().Should().Be("device-token");
        Prop(Prop(msg, "notification"), "title").GetString().Should().Be("Title Here");
        Prop(Prop(msg, "notification"), "body").GetString().Should().Be("Body here");
    }

    [Fact]
    public async Task Fcm_UnconfiguredProject_ThrowsBeforeSending()
    {
        var handler = new CapturingHandler();
        var options = Options.Create(new FcmNotificationOptions()); // ProjectId = ""
        var sender = new FcmPushSender(new HttpClient(handler), options, new FakeFcmTokenProvider("unused"));

        var act = () => sender.SendAsync(new PushMessage("device-token", "t", "b"));

        await act.Should().ThrowAsync<InvalidOperationException>();
        handler.Request.Should().BeNull(); // failed fast — no HTTP call made
    }

    private sealed class FakeFcmTokenProvider(string token) : IFcmAccessTokenProvider
    {
        public Task<string> GetAccessTokenAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(token);
    }
}
