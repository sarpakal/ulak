using Messenger.Core;
using Messenger.Core.DTOs;
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

        public MessagesController(MessengerService messengerService, ILogger<MessagesController> logger)
        {
            _messengerService = messengerService;
            _logger = logger;
        }

        [HttpPost("email")]
        public async Task<IActionResult> SendEmail([FromBody] EmailMessage msg, CancellationToken ct)
        {
            _logger.LogInformation("SendEmail request for recipients={Recipients}", msg.To);
            await _messengerService.SendEmailAsync(msg, ct);
            _logger.LogInformation("SendEmail completed for recipients={Recipients}", msg.To);
            return Ok(new { status = "Email sent" });
        }

        [HttpPost("sms")]
        public async Task<IActionResult> SendSms([FromBody] SmsMessage msg, CancellationToken ct)
        {
            _logger.LogInformation("SendSms request to={Recipient}", msg.To);
            await _messengerService.SendSmsAsync(msg, ct);
            _logger.LogInformation("SendSms completed to={Recipient}", msg.To);
            return Ok(new { status = "SMS sent" });
        }

        [HttpPost("whatsapp")]
        public async Task<IActionResult> SendWhatsapp([FromBody] WhatsAppMessage msg, CancellationToken ct)
        {
            _logger.LogInformation("SendWhatsapp request to={Recipient}", msg.To);
            await _messengerService.SendWhatsappAsync(msg, ct);
            _logger.LogInformation("SendWhatsapp completed to={Recipient}", msg.To);
            return Ok(new { status = "WhatsApp sent" });
        }

        [HttpPost("push")]
        public async Task<IActionResult> SendPush([FromBody] PushMessage msg, CancellationToken ct)
        {
            _logger.LogInformation("SendPush request to={Recipient}", msg.To);
            await _messengerService.SendPushAsync(msg, ct);
            _logger.LogInformation("SendPush completed to={Recipient}", msg.To);
            return Ok(new { status = "Push sent" });
        }

        [HttpGet("/")]
        [ProducesResponseType(typeof(string), StatusCodes.Status200OK)]
        public IActionResult Index()
        {
            return Ok("Messenger is running.");
        }
    }
}
