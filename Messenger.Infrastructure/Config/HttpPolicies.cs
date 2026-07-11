using System;
using System.Net.Http;
using Microsoft.Extensions.Http.Resilience;
using Polly;
using Polly.Extensions.Http;

namespace Messenger.Infrastructure.Config;

// Public so tests can build the exact production FCM pipeline (no InternalsVisibleTo in this repo).
public static class HttpPolicies
{
    public static IAsyncPolicy<HttpResponseMessage> GetRetryPolicy()
    {
        // Retry on transient failures and 429 Too Many Requests
        return HttpPolicyExtensions
            .HandleTransientHttpError()
            .OrResult(msg => (int)msg.StatusCode == 429)
            .WaitAndRetryAsync(3, retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)));
    }

    public static IAsyncPolicy<HttpResponseMessage> GetTimeoutPolicy()
    {
        // Per-request timeout
        return Policy.TimeoutAsync<HttpResponseMessage>(TimeSpan.FromSeconds(10));
    }

    /// <summary>
    /// Polly v8 resilience pipeline for the FCM client (behavioral parity with the legacy
    /// policies above, plus native Retry-After handling). Deliberately NOT
    /// AddStandardResilienceHandler — that would add a circuit breaker, rate limiter and
    /// total timeout, i.e. new untested production behavior.
    /// </summary>
    public static void ConfigureFcmResilience(ResiliencePipelineBuilder<HttpResponseMessage> builder)
    {
        builder.AddRetry(new HttpRetryStrategyOptions
        {
            MaxRetryAttempts = 3,
            BackoffType = DelayBackoffType.Exponential,
            UseJitter = true,
            Delay = TimeSpan.FromSeconds(1),
            // Defaults kept deliberately:
            //  - ShouldHandle = HttpClientResiliencePredicates.IsTransient:
            //    HttpRequestException, TimeoutRejectedException, 5xx, 408, 429.
            //    400/404 are NOT transient → dead-token responses are never retried.
            //  - ShouldRetryAfterHeader = true: when FCM sends Retry-After it overrides
            //    the computed backoff — the native Polly v8 429 handling.
        });
        builder.AddTimeout(TimeSpan.FromSeconds(10)); // per-attempt; retry wraps timeout
    }
}
