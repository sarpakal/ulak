using System.Net;
using FluentAssertions;
using Messenger.Infrastructure.Config;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Messenger.Tests;

/// <summary>
/// Exercises the production FCM resilience pipeline (<see cref="HttpPolicies.ConfigureFcmResilience"/>)
/// through a real HttpClientFactory registration: 429s are retried (honoring Retry-After — a
/// 0-second header makes the test fast AND proves the header overrides exponential backoff),
/// while dead-token responses (400/404) surface immediately without a single retry.
/// </summary>
public class FcmResilienceTests
{
    // Returns the queued responses in order; repeats the last one if attempts exceed the queue.
    private sealed class SequenceHandler(params HttpResponseMessage[] responses) : HttpMessageHandler
    {
        public int Attempts { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = responses[Math.Min(Attempts, responses.Length - 1)];
            Attempts++;
            return Task.FromResult(response);
        }
    }

    private static HttpClient CreateClient(SequenceHandler handler)
    {
        var services = new ServiceCollection();
        services.AddHttpClient("fcm")
            .ConfigurePrimaryHttpMessageHandler(() => handler)
            .AddResilienceHandler("fcm-pipeline", HttpPolicies.ConfigureFcmResilience);
        return services.BuildServiceProvider()
            .GetRequiredService<IHttpClientFactory>()
            .CreateClient("fcm");
    }

    private static HttpResponseMessage TooManyRequests(int retryAfterSeconds)
    {
        var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
        response.Headers.RetryAfter =
            new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(retryAfterSeconds));
        return response;
    }

    [Fact]
    public async Task Retries429_HonoringRetryAfter_ThenSucceeds()
    {
        var handler = new SequenceHandler(
            TooManyRequests(0),
            TooManyRequests(0),
            new HttpResponseMessage(HttpStatusCode.OK));
        var client = CreateClient(handler);
        var started = DateTime.UtcNow;

        var response = await client.GetAsync("https://fcm.test/send");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        handler.Attempts.Should().Be(3);
        // Retry-After: 0 must override the exponential backoff (1s/2s/4s + jitter) — if the
        // computed delay were used instead, two waits would take multiple seconds.
        (DateTime.UtcNow - started).Should().BeLessThan(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task DoesNotRetry_404_DeadToken()
    {
        var handler = new SequenceHandler(new HttpResponseMessage(HttpStatusCode.NotFound));
        var client = CreateClient(handler);

        var response = await client.GetAsync("https://fcm.test/send");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        handler.Attempts.Should().Be(1);
    }

    [Fact]
    public async Task DoesNotRetry_400_MalformedToken()
    {
        var handler = new SequenceHandler(new HttpResponseMessage(HttpStatusCode.BadRequest));
        var client = CreateClient(handler);

        var response = await client.GetAsync("https://fcm.test/send");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        handler.Attempts.Should().Be(1);
    }

    [Fact]
    public async Task RetriesExhausted_SurfaceFinal429()
    {
        var handler = new SequenceHandler(TooManyRequests(0));
        var client = CreateClient(handler);

        var response = await client.GetAsync("https://fcm.test/send");

        response.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        handler.Attempts.Should().Be(4); // 1 initial + MaxRetryAttempts = 3
    }
}
