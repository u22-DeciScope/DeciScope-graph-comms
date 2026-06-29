using EchoBot.Bot;
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
        private readonly IBotService botService;
        private readonly ILogger<BotControlController> logger;

        public BotControlController(
            BotControlOptions options,
            IBotJoinCommandService joinCommandService,
            IBotService botService,
            ILogger<BotControlController> logger)
        {
            this.options = options;
            this.joinCommandService = joinCommandService;
            this.botService = botService;
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
                "Join command received. SessionId={SessionId}; MeetingUrlHash={MeetingUrlHash}; JoinMeetingId={JoinMeetingId}; CandidateUserIdsCount={CandidateUserIdsCount}; CandidateUserIdsHash={CandidateUserIdsHash}; CandidateUserPrincipalNamesCount={CandidateUserPrincipalNamesCount}; CandidateUserPrincipalNamesHash={CandidateUserPrincipalNamesHash}; CallId={CallId}",
                command.SessionId,
                HashForLog(command.JoinUrl),
                command.JoinMeetingId,
                command.CandidateUserIds?.Count ?? 0,
                HashesForLog(command.CandidateUserIds),
                command.CandidateUserPrincipalNames?.Count ?? 0,
                HashesForLog(command.CandidateUserPrincipalNames),
                null);

            var result = joinCommandService.TryEnqueue(command);
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

        [HttpPost("/internal/bot/meeting-sessions/{sessionId}/end")]
        public async Task<IActionResult> EndMeetingSession(string sessionId, [FromBody] BotEndCommand? command, CancellationToken cancellationToken)
        {
            if (!options.ControlApiEnabled)
            {
                logger.LogWarning("Bot control end rejected because control API is disabled. SessionId={SessionId}; JoinMode={JoinMode}", sessionId, options.JoinMode);
                return StatusCode((int)HttpStatusCode.NotFound);
            }

            if (!IsAuthorized())
            {
                logger.LogWarning("Bot control end rejected due to invalid token. SessionId={SessionId}", sessionId);
                return Unauthorized();
            }

            var routeSessionId = sessionId?.Trim();
            var bodySessionId = command?.SessionId?.Trim();
            if (string.IsNullOrWhiteSpace(routeSessionId))
            {
                return BadRequest(new { error = "invalid_request", message = "sessionId is required." });
            }
            if (!string.IsNullOrWhiteSpace(bodySessionId)
                && !string.Equals(routeSessionId, bodySessionId, StringComparison.OrdinalIgnoreCase))
            {
                return BadRequest(new { error = "invalid_request", message = "route sessionId and body sessionId must match." });
            }

            var reason = string.IsNullOrWhiteSpace(command?.Reason)
                ? "manual_end_requested"
                : command!.Reason!.Trim();
            logger.LogInformation(
                "End command received. SessionId={SessionId}; BotCallId={BotCallId}; Reason={Reason}",
                routeSessionId,
                command?.BotCallId,
                reason);

            var activeCallFound = await botService.EndMeetingSessionAsync(routeSessionId, reason, cancellationToken).ConfigureAwait(false);
            logger.LogInformation(
                "Bot control end command accepted. SessionId={SessionId}; ActiveCallFound={ActiveCallFound}",
                routeSessionId,
                activeCallFound);

            return Accepted(new
            {
                accepted = true,
                activeCallFound,
                sessionId = routeSessionId,
            });
        }

        private static string HashForLog(string value)
        {
            var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
            return Convert.ToHexString(bytes, 0, 8);
        }

        private static string HashesForLog(IEnumerable<string>? values)
        {
            if (values == null)
            {
                return "[]";
            }

            var hashes = values
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => HashForLog(value.Trim()))
                .ToArray();
            return hashes.Length == 0 ? "[]" : $"[{string.Join(",", hashes)}]";
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
