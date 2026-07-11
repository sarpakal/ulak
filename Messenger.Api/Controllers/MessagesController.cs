using Messenger.Core;
using Messenger.Core.DTOs;
using Messenger.Core.Exceptions;
using Messenger.Infrastructure.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Mvc;

namespace Messenger.Api.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class MessagesController : ControllerBase
    {
        private readonly MessengerService _messengerService;
        private readonly ILogger<MessagesController> _logger;
        private readonly MessengerDbContext _db;

        public MessagesController(MessengerService messengerService, ILogger<MessagesController> logger, MessengerDbContext db)
        {
            _messengerService = messengerService;
            _logger = logger;
            _db = db;
        }

        [HttpPost("email")]
        public async Task<IActionResult> SendEmail([FromBody] EmailMessage msg, CancellationToken ct)
        {
            _logger.LogInformation("SendEmail request for recipients={Recipients}", msg.To);
            var status = "Sent";
            try
            {
                await _messengerService.SendEmailAsync(msg, ct);
            }
            catch
            {
                status = "Failed";
                throw;
            }
            finally
            {
                await WriteLogAsync("Email", string.Join(",", msg.To), msg.Subject, status);
            }
            _logger.LogInformation("SendEmail completed for recipients={Recipients}", msg.To);
            return Ok(new { status = "Email sent" });
        }

        [HttpPost("sms")]
        public async Task<IActionResult> SendSms([FromBody] SmsMessage msg, CancellationToken ct)
        {
            _logger.LogInformation("SendSms request to={Recipient}", msg.To);
            var status = "Sent";
            try
            {
                await _messengerService.SendSmsAsync(msg, ct);
            }
            catch
            {
                status = "Failed";
                throw;
            }
            finally
            {
                await WriteLogAsync("SMS", string.Join(",", msg.To), msg.Text, status);
            }
            _logger.LogInformation("SendSms completed to={Recipient}", msg.To);
            return Ok(new { status = "SMS sent" });
        }

        [HttpPost("whatsapp")]
        public async Task<IActionResult> SendWhatsapp([FromBody] WhatsAppMessage msg, CancellationToken ct)
        {
            _logger.LogInformation("SendWhatsapp request to={Recipient}", msg.To);
            var status = "Sent";
            try
            {
                await _messengerService.SendWhatsappAsync(msg, ct);
            }
            catch
            {
                status = "Failed";
                throw;
            }
            finally
            {
                await WriteLogAsync("WhatsApp", msg.To, msg.Text, status);
            }
            _logger.LogInformation("SendWhatsapp completed to={Recipient}", msg.To);
            return Ok(new { status = "WhatsApp sent" });
        }

        [HttpPost("push")]
        public async Task<IActionResult> SendPush([FromBody] PushMessage msg, CancellationToken ct)
        {
            _logger.LogInformation("SendPush request to={Recipient}", msg.To);
            var status = "Sent";
            string? errorCode = null;
            try
            {
                await _messengerService.SendPushAsync(msg, ct);
            }
            catch (PushSendException ex)
            {
                // Classified provider failure — map to an honest status code so callers
                // can react (410 → delete token, 429 → back off, 502 → retry later).
                status = "Failed";
                errorCode = ex.ProviderErrorCode ?? ex.Reason.ToString();
                _logger.LogWarning(ex, "SendPush failed to={Recipient} reason={Reason} code={Code}",
                    msg.To, ex.Reason, errorCode);
                return MapPushFailure(ex, errorCode);
            }
            catch
            {
                status = "Failed";
                throw;
            }
            finally
            {
                await WriteLogAsync("Push", msg.To, msg.Title, status, errorCode);
            }
            _logger.LogInformation("SendPush completed to={Recipient}", msg.To);
            return Ok(new { status = "Push sent" });
        }

        private ObjectResult MapPushFailure(PushSendException ex, string errorCode)
        {
            var body = new { status = "failed", errorCode, detail = ex.Message };
            switch (ex.Reason)
            {
                case PushSendFailureReason.InvalidToken:
                    return StatusCode(StatusCodes.Status410Gone, body);
                case PushSendFailureReason.QuotaExceeded:
                    if (ex.RetryAfter is { } retryAfter && retryAfter > TimeSpan.Zero)
                        Response.Headers.RetryAfter = ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString();
                    return StatusCode(StatusCodes.Status429TooManyRequests, body);
                default:
                    return StatusCode(StatusCodes.Status502BadGateway, body);
            }
        }

        private async Task WriteLogAsync(string channel, string recipient, string? payload, string status,
            string? errorCode = null)
        {
            try
            {
                _db.MessageLogs.Add(new MessageLog
                {
                    Channel = channel,
                    Recipient = recipient,
                    Payload = payload,
                    Status = status,
                    ErrorCode = errorCode,
                    CorrelationId = HttpContext.TraceIdentifier,
                });
                var saved = await _db.SaveChangesAsync(CancellationToken.None);
                _logger.LogInformation("MessageLog saved {Count} row(s) for channel={Channel}", saved, channel);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "MessageLog write failed for channel={Channel}", channel);
            }
        }

        [HttpGet("/")]
        [ProducesResponseType(typeof(string), StatusCodes.Status200OK)]
        public IActionResult Index()
        {
            return Ok("Messenger is running.");
        }
    }
}
