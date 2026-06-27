using EchoBot.Models;
using Microsoft.Graph;
using Microsoft.Graph.Contracts;
using Microsoft.Graph.Models;
using System.Text.Json;

namespace EchoBot.Meetings
{
    public sealed class TeamsMeetingJoinInfoProvider : ITeamsMeetingJoinInfoProvider, IDisposable
    {
        private readonly TeamsMeetingUrlResolver _urlResolver;
        private readonly ILogger<TeamsMeetingJoinInfoProvider> _logger;

        public TeamsMeetingJoinInfoProvider(ILogger<TeamsMeetingJoinInfoProvider> logger)
            : this(new TeamsMeetingUrlResolver(), logger)
        {
        }

        public TeamsMeetingJoinInfoProvider(
            TeamsMeetingUrlResolver urlResolver,
            ILogger<TeamsMeetingJoinInfoProvider> logger)
        {
            _urlResolver = urlResolver;
            _logger = logger;
        }

        public async Task<TeamsMeetingJoinInfo> GetJoinInfoAsync(JoinCallBody joinCallBody, CancellationToken cancellationToken)
        {
            if (joinCallBody == null)
            {
                throw new TeamsMeetingJoinException("missing_body", "A request body is required.");
            }

            var requestedUrl = joinCallBody.GetMeetingUrl();
            var (resolvedUrl, redirected) = await _urlResolver.ResolveAsync(requestedUrl, cancellationToken).ConfigureAwait(false);

            try
            {
                var (chatInfo, meetingInfo) = JoinInfo.ParseJoinURL(resolvedUrl.ToString(), joinCallBody.TenantId);
                var tenantId = ResolveTenantId(joinCallBody.TenantId, meetingInfo);

                if (meetingInfo is JoinMeetingIdMeetingInfo && string.IsNullOrWhiteSpace(tenantId))
                {
                    throw new TeamsMeetingJoinException("missing_tenant_id", "tenantId is required when using a Teams /meet/{meetingId}?p={passcode} URL.");
                }

                _logger.LogInformation(
                    "Teams meeting URL parsed successfully. Format={Format}; Redirected={Redirected}",
                    meetingInfo.GetType().Name,
                    redirected);

                return new TeamsMeetingJoinInfo(chatInfo, meetingInfo, tenantId, resolvedUrl, redirected);
            }
            catch (JsonException ex)
            {
                throw new TeamsMeetingJoinException("invalid_context", "The Teams meeting URL context could not be parsed.", ex);
            }
            catch (ArgumentException ex)
            {
                throw new TeamsMeetingJoinException("unsupported_meeting_url", ex.Message, ex);
            }
        }

        public void Dispose()
        {
            _urlResolver.Dispose();
        }

        private static string? ResolveTenantId(string? requestedTenantId, MeetingInfo meetingInfo)
        {
            if (!string.IsNullOrWhiteSpace(requestedTenantId))
            {
                return requestedTenantId;
            }

            return (meetingInfo as OrganizerMeetingInfo)?.Organizer.GetPrimaryIdentity()?.GetTenantId();
        }
    }
}
