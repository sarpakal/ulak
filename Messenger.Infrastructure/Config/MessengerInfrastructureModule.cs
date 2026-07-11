using Messenger.Core.Models;
using Messenger.Core;
using Messenger.Core.Interfaces;
using Messenger.Core.Options;
using Messenger.Infrastructure.Senders;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Messenger.Infrastructure.Config
{
    public static class MessengerInfrastructureModule
    {
        public static IServiceCollection AddMessengerInfrastructure(
            this IServiceCollection services,
            IConfiguration configuration)
        {
            // ── Options ───────────────────────────────────────────────────────
            services.Configure<CorvassOptions>(configuration.GetSection(CorvassOptions.SectionName));
            services.Configure<TwilioOptions>(configuration.GetSection(TwilioOptions.SectionName));
            services.Configure<SmsOptions>(configuration.GetSection(SmsOptions.SectionName));

            // ── SMS senders ───────────────────────────────────────────────────
            // CorvassSmsSender needs a typed HttpClient; registered as concrete type
            // so RoutingSmsSender can inject it directly.
            services.AddHttpClient<CorvassSmsSender>()
                .AddPolicyHandler(HttpPolicies.GetRetryPolicy())
                .AddPolicyHandler(HttpPolicies.GetTimeoutPolicy())
                .SetHandlerLifetime(TimeSpan.FromMinutes(5));

            services.AddScoped<TwilioSmsSender>();
            services.AddScoped<ConsoleSmsService>();

            // RoutingSmsSender is the public ISmsSender — dispatches to the above by prefix
            services.AddScoped<ISmsSender, RoutingSmsSender>();

            // ── Other channel senders ─────────────────────────────────────────
            services.AddScoped<IEmailSender, SmtpEmailSender>();

            services.AddHttpClient<IWhatsAppMessageSender, WhatsappSender>()
                .AddPolicyHandler(HttpPolicies.GetRetryPolicy())
                .AddPolicyHandler(HttpPolicies.GetTimeoutPolicy())
                .SetHandlerLifetime(TimeSpan.FromMinutes(5));

            // FCM HTTP v1: OAuth2 token provider is a singleton (caches the Google credential).
            services.AddSingleton<IFcmAccessTokenProvider, GoogleFcmAccessTokenProvider>();
            // Polly v8 resilience handler (native Retry-After on 429) — FCM only;
            // Corvass/WhatsApp above stay on the legacy policy pair.
            services.AddHttpClient<IPushNotificationSender, FcmPushSender>()
                .SetHandlerLifetime(TimeSpan.FromMinutes(5))
                .AddResilienceHandler("fcm-pipeline", HttpPolicies.ConfigureFcmResilience);

            // ── Messenger facade ──────────────────────────────────────────────
            services.AddScoped<MessengerService>();

            return services;
        }
    }
}
