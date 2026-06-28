using EchoBot.Services;
using Microsoft.AspNetCore.Mvc;
using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace EchoBot.Controllers
{
    [ApiController]
    public sealed class BotControlController : ControllerBase
    {
        private const string ControlTokenHeaderName = "X-DeciScope-Bot-Control-Token";

        private readonly BotControlOptions options;
        private readonly IBotJoinCommandService joinCommandService;
        private readonly ILogger<BotControlController> logger;

        public BotControlController(
            BotControlOptions options,
            IBotJoinCommandService joinCommandService,
            ILogger<BotControlController> logger)
        {
            this.options = options;
            this.joinCommandService = joinCommandService;
            this.logger = logger;
        }

        [HttpGet("/healthz")]
        public IActionResult Healthz()
        {
            return Ok(new
            {
                status = "ok",
                joinMode = options.JoinMode.ToString(),
                controlApiEnabled = options.ControlApiEnabled,
            });
        }

        [HttpPost("/internal/bot/join")]
        public IActionResult Join([FromBody] BotJoinCommand command)
        {
            if (!options.ControlApiEnabled)
            {
                logger.LogWarning("Bot control join rejected because control API is disabled. SessionId={SessionId}; JoinMode={JoinMode}", command?.SessionId, options.JoinMode);
                return StatusCode((int)HttpStatusCode.NotFound);
            }

            if (!IsAuthorized())
            {
                logger.LogWarning("Bot control join rejected due to invalid token. SessionId={SessionId}", command?.SessionId);
                return Unauthorized();
            }

            if (command == null || string.IsNullOrWhiteSpace(command.SessionId) || string.IsNullOrWhiteSpace(command.JoinUrl))
            {
                return BadRequest(new { error = "invalid_request", message = "sessionId and joinUrl are required." });
            }

            logger.LogInformation(
                "Join command received. SessionId={SessionId}; MeetingUrlHash={MeetingUrlHash}; CallId={CallId}",
                command.SessionId,
                HashForLog(command.JoinUrl),
                null);

            var result = joinCommandService.TryEnqueue(command.SessionId, command.JoinUrl, command.TenantId);
            if (!result.Accepted)
            {
                return BadRequest(new { error = "join_command_rejected", message = result.Reason });
            }

            logger.LogInformation(
                "Bot control join command accepted. SessionId={SessionId}; Duplicate={Duplicate}",
                command.SessionId,
                result.Duplicate);

            return Accepted(new
            {
                accepted = true,
                duplicate = result.Duplicate,
                sessionId = command.SessionId,
            });
        }

        private static string HashForLog(string value)
        {
            var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
            return Convert.ToHexString(bytes, 0, 8);
        }

        private bool IsAuthorized()
        {
            if (string.IsNullOrWhiteSpace(options.ControlToken))
            {
                return false;
            }

            if (!Request.Headers.TryGetValue(ControlTokenHeaderName, out var values))
            {
                return false;
            }

            var suppliedToken = values.FirstOrDefault();
            if (string.IsNullOrWhiteSpace(suppliedToken))
            {
                return false;
            }

            var expectedBytes = Encoding.UTF8.GetBytes(options.ControlToken);
            var suppliedBytes = Encoding.UTF8.GetBytes(suppliedToken);
            return suppliedBytes.Length == expectedBytes.Length
                && CryptographicOperations.FixedTimeEquals(suppliedBytes, expectedBytes);
        }
    }
}
