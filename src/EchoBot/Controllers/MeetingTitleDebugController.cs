using EchoBot.Meetings;
using EchoBot.Models;
using EchoBot.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Graph.Models;
using System.Security.Cryptography;
using System.Text;

namespace EchoBot.Controllers
{
    [ApiController]
    public sealed class MeetingTitleDebugController : ControllerBase
    {
        private const string ControlTokenHeaderName = "X-DeciScope-Bot-Control-Token";

        private readonly BotControlOptions options;
        private readonly ITeamsMeetingJoinInfoProvider joinInfoProvider;
        private readonly ITeamsMeetingTitleResolver titleResolver;
        private readonly ILogger<MeetingTitleDebugController> logger;

        public MeetingTitleDebugController(
            BotControlOptions options,
            ITeamsMeetingJoinInfoProvider joinInfoProvider,
            ITeamsMeetingTitleResolver titleResolver,
            ILogger<MeetingTitleDebugController> logger)
        {
            this.options = options;
            this.joinInfoProvider = joinInfoProvider;
            this.titleResolver = titleResolver;
            this.logger = logger;
        }

        [HttpPost("/api/v1/debug/resolve-meeting-title")]
        public async Task<IActionResult> Resolve([FromBody] MeetingTitleDebugRequest request, CancellationToken cancellationToken)
        {
            if (!IsAuthorized())
            {
                return Unauthorized();
            }

            if (request == null || string.IsNullOrWhiteSpace(request.JoinUrl))
            {
                return BadRequest(new { error = "invalid_request", message = "joinUrl is required." });
            }

            TeamsMeetingJoinInfo? joinInfo = null;
            var attempts = new List<object>();
            try
            {
                joinInfo = await joinInfoProvider.GetJoinInfoAsync(
                    new JoinCallBody
                    {
                        JoinUrl = request.JoinUrl,
                        TenantId = request.TenantId,
                    },
                    cancellationToken).ConfigureAwait(false);

                attempts.Add(new
                {
                    method = "resolve_short_url",
                    result = "succeeded",
                    canonicalJoinWebUrlFound = !string.Equals(request.JoinUrl?.Trim(), joinInfo.ResolvedJoinUrl.ToString(), StringComparison.OrdinalIgnoreCase),
                    redirected = joinInfo.Redirected,
                });
            }
            catch (TeamsMeetingJoinException ex)
            {
                logger.LogWarning(
                    ex,
                    "Meeting title debug join URL resolution failed. JoinUrlHash={JoinUrlHash}; ErrorCode={ErrorCode}; Reason={Reason}",
                    HashForLog(request.JoinUrl),
                    ex.Code,
                    ex.Message);
                attempts.Add(new
                {
                    method = "resolve_short_url",
                    result = "failed",
                    errorCode = ex.Code,
                    message = ex.Message,
                });
            }

            var resolvedJoinUrl = joinInfo?.ResolvedJoinUrl.ToString() ?? request.JoinUrl;
            var joinMeetingId = FirstNonEmpty(request.JoinMeetingId, ExtractJoinMeetingId(joinInfo?.MeetingInfo));
            var organizerId = ExtractOrganizerId(joinInfo?.MeetingInfo);
            var tenantId = FirstNonEmpty(request.TenantId, joinInfo?.TenantId);

            logger.LogInformation(
                "Meeting title debug resolution started. JoinUrlHash={JoinUrlHash}; TenantIdConfigured={TenantIdConfigured}; JoinMeetingId={JoinMeetingId}; OrganizerId={OrganizerId}; CandidateUserIdCount={CandidateUserIdCount}",
                HashForLog(resolvedJoinUrl),
                !string.IsNullOrWhiteSpace(tenantId),
                joinMeetingId,
                organizerId,
                request.CandidateUserIds?.Count ?? 0);

            var result = await titleResolver.ResolveAsync(
                new TeamsMeetingTitleResolutionRequest
                {
                    SessionId = "debug",
                    TenantId = tenantId,
                    OriginalJoinUrl = request.JoinUrl,
                    ResolvedJoinUrl = resolvedJoinUrl,
                    JoinUrlHash = HashForLog(resolvedJoinUrl),
                    ThreadId = joinInfo?.ChatInfo.ThreadId,
                    JoinMeetingId = joinMeetingId,
                    OrganizerId = organizerId,
                    CandidateUserIds = request.CandidateUserIds,
                    Stage = "debug",
                },
                cancellationToken).ConfigureAwait(false);

            attempts.Add(new
            {
                method = "resolve_graph_title",
                result = result.HasTitle ? "succeeded" : "failed",
                errorCode = result.ErrorCode,
                message = result.ErrorMessage,
            });

            return Ok(new
            {
                title = result.Title,
                titleSource = result.TitleSource,
                provider = result.Provider,
                joinMeetingId = result.JoinMeetingId ?? joinMeetingId,
                canonicalJoinWebUrl = result.CanonicalJoinWebUrl ?? resolvedJoinUrl,
                organizerId = result.OrganizerId ?? organizerId,
                organizerName = result.OrganizerName,
                organizerEmail = result.OrganizerEmail,
                scheduledStartAt = result.ScheduledStartAt,
                scheduledEndAt = result.ScheduledEndAt,
                titleResolutionErrorCode = result.ErrorCode,
                titleResolutionErrorMessage = result.ErrorMessage,
                attempts,
            });
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

        private static string? ExtractJoinMeetingId(MeetingInfo? meetingInfo)
        {
            return meetingInfo is JoinMeetingIdMeetingInfo joinMeetingId
                ? joinMeetingId.JoinMeetingId
                : null;
        }

        private static string? ExtractOrganizerId(MeetingInfo? meetingInfo)
        {
            return meetingInfo is OrganizerMeetingInfo organizerMeetingInfo
                ? organizerMeetingInfo.Organizer?.User?.Id
                : null;
        }

        private static string? FirstNonEmpty(params string?[] values)
        {
            foreach (var value in values)
            {
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value.Trim();
                }
            }
            return null;
        }

        private static string HashForLog(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value.Trim()));
            return Convert.ToHexString(bytes, 0, 8);
        }
    }

    public sealed class MeetingTitleDebugRequest
    {
        public string? JoinUrl { get; init; }

        public string? JoinMeetingId { get; init; }

        public string? TenantId { get; init; }

        public IReadOnlyCollection<string>? CandidateUserIds { get; init; }
    }
}
