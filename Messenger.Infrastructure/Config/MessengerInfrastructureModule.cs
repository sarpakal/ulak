using System;
using AuthApi.Models;
using Messenger.Core;
using Messenger.Core.Interfaces;
using Messenger.Core.Options;
using Messenger.Infrastructure.Senders;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;

namespace Messenger.Infrastructure.Config
{
    public static class MessengerInfrastructureModule
    {
        public static IServiceCollection AddMessengerInfrastructure(this IServiceCollection services)
        {
            // ── Senders — Scoped to match MessengerService lifetime ───────
            services.AddScoped<IEmailSender, SmtpEmailSender>();

            // HTTP-based senders use typed HttpClient with retry/timeout policies
            services.AddHttpClient<Messenger.Core.Interfaces.IWhatsAppMessageSender, WhatsappSender>()
                .AddPolicyHandler(HttpPolicies.GetRetryPolicy())
                .AddPolicyHandler(HttpPolicies.GetTimeoutPolicy())
                .SetHandlerLifetime(TimeSpan.FromMinutes(5));

            services.AddHttpClient<Messenger.Core.Interfaces.IPushNotificationSender, FcmPushSender>()
                .AddPolicyHandler(HttpPolicies.GetRetryPolicy())
                .AddPolicyHandler(HttpPolicies.GetTimeoutPolicy())
                .SetHandlerLifetime(TimeSpan.FromMinutes(5));

            services.AddHttpClient<Messenger.Core.Interfaces.ISmsSender, CorvassSmsSender>()
                .AddPolicyHandler(HttpPolicies.GetRetryPolicy())
                .AddPolicyHandler(HttpPolicies.GetTimeoutPolicy())
                .SetHandlerLifetime(TimeSpan.FromMinutes(5));

            // ── Messenger facade ──────────────────────────────────────────
            services.AddScoped<MessengerService>();

            return services;
        }
    }
}