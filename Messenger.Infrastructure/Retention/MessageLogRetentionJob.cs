using Messenger.Core.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Messenger.Infrastructure.Retention;

/// <summary>
/// Background service that periodically deletes aged <c>MessageLogs</c> rows (PII cleanup).
/// Runs once shortly after startup, then every <c>RunIntervalHours</c>. Mirrors the platform
/// pattern (Auth.Api's AuditRetentionJob): resolve the scoped service per run, never crash the host.
/// </summary>
public class MessageLogRetentionJob : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly MessageLogRetentionOptions _options;
    private readonly ILogger<MessageLogRetentionJob> _logger;

    public MessageLogRetentionJob(
        IServiceScopeFactory scopeFactory,
        IOptions<MessageLogRetentionOptions> options,
        ILogger<MessageLogRetentionJob> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation("MessageLog retention job disabled (MessageLogRetention:Enabled=false).");
            return;
        }

        // Short startup delay so the app (and DB migration) is fully ready before the first run.
        try { await Task.Delay(TimeSpan.FromSeconds(30), ct); }
        catch (OperationCanceledException) { return; }

        var interval = TimeSpan.FromHours(Math.Max(1, _options.RunIntervalHours));
        _logger.LogInformation(
            "MessageLog retention job scheduled — every {Hours}h, retaining {Days} days.",
            _options.RunIntervalHours, _options.RetentionDays);

        while (!ct.IsCancellationRequested)
        {
            await RunOnce(ct);
            try { await Task.Delay(interval, ct); }
            catch (OperationCanceledException) { break; }
        }
    }

    private async Task RunOnce(CancellationToken ct)
    {
        try
        {
            // IMessageLogRetentionService is scoped (it depends on the scoped DbContext).
            await using var scope = _scopeFactory.CreateAsyncScope();
            var service = scope.ServiceProvider.GetRequiredService<IMessageLogRetentionService>();
            await service.RunAsync(ct);
        }
        catch (OperationCanceledException)
        {
            // App is shutting down — exit cleanly.
        }
        catch (Exception ex)
        {
            // Log but don't crash the host — the next run will try again.
            _logger.LogError(ex, "Unexpected error in MessageLog retention job");
        }
    }
}
