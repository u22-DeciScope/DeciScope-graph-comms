using EchoBot.Models;
using Microsoft.Graph;
using Microsoft.Graph.Contracts;
using Microsoft.Graph.Models;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace EchoBot.Meetings
{
    public sealed class TeamsMeetingJoinInfoProvider : ITeamsMeetingJoinInfoProvider, IDisposable
    {
        private readonly TeamsMeetingUrlResolver _urlResolver;
        private readonly ILogger<TeamsMeetingJoinInfoProvider> _logger;
        private readonly MeetingJoinOptions _options;

        public TeamsMeetingJoinInfoProvider(ILogger<TeamsMeetingJoinInfoProvider> logger)
            : this(new TeamsMeetingUrlResolver(), MeetingJoinOptions.FromEnvironment(), logger)
        {
        }

        public TeamsMeetingJoinInfoProvider(
            TeamsMeetingUrlResolver urlResolver,
            ILogger<TeamsMeetingJoinInfoProvider> logger)
            : this(urlResolver, MeetingJoinOptions.FromEnvironment(), logger)
        {
        }

        public TeamsMeetingJoinInfoProvider(
            TeamsMeetingUrlResolver urlResolver,
            MeetingJoinOptions options,
            ILogger<TeamsMeetingJoinInfoProvider> logger)
        {
            _urlResolver = urlResolver;
            _options = options;
            _logger = logger;
        }

        public async Task<TeamsMeetingJoinInfo> GetJoinInfoAsync(JoinCallBody joinCallBody, CancellationToken cancellationToken)
        {
            if (joinCallBody == null)
            {
                throw new TeamsMeetingJoinException("missing_body", "A request body is required.");
            }

            var requestedUrl = joinCallBody.GetMeetingUrl();
            var shortUrl = IsShortMeetingUrl(requestedUrl);
            if (shortUrl)
            {
                _logger.LogInformation(
                    "Short Teams meeting URL resolution started. OriginalUrlHash={OriginalUrlHash}",
                    HashForLog(requestedUrl));
            }

            Uri resolvedUrl;
            bool redirected;
            try
            {
                (resolvedUrl, redirected) = await _urlResolver.ResolveAsync(requestedUrl, cancellationToken).ConfigureAwait(false);
            }
            catch (TeamsMeetingJoinException ex) when (shortUrl)
            {
                _logger.LogWarning(
                    ex,
                    "Short Teams meeting URL resolution failed. OriginalUrlHash={OriginalUrlHash}; Reason={Reason}; ErrorCode={ErrorCode}",
                    HashForLog(requestedUrl),
                    ex.Message,
                    ex.Code);
                throw;
            }

            var resolvedContext = ExtractContextIdentifiers(resolvedUrl);
            if (shortUrl)
            {
                _logger.LogInformation(
                    "Short Teams meeting URL resolution succeeded. OriginalUrlHash={OriginalUrlHash}; CanonicalJoinWebUrlHash={CanonicalJoinWebUrlHash}; Redirected={Redirected}; ExtractedTid={ExtractedTid}; ExtractedOid={ExtractedOid}",
                    HashForLog(requestedUrl),
                    HashForLog(resolvedUrl.ToString()),
                    redirected,
                    MeetingTenantValidator.Suffix(resolvedContext.Tid),
                    resolvedContext.Oid);
            }

            try
            {
                var (chatInfo, meetingInfo, parsedUrlSource) = ParseJoinUrlWithFallback(resolvedUrl, requestedUrl, joinCallBody.TenantId);
                var defaultTenantIdUsed = false;
                var resolvedContextTenantIdUsed = false;
                var tenantId = ResolveTenantId(joinCallBody.TenantId, meetingInfo);

                if (meetingInfo is JoinMeetingIdMeetingInfo && string.IsNullOrWhiteSpace(tenantId) && !string.IsNullOrWhiteSpace(resolvedContext.Tid))
                {
                    tenantId = resolvedContext.Tid;
                    resolvedContextTenantIdUsed = true;
                }

                if (meetingInfo is JoinMeetingIdMeetingInfo && string.IsNullOrWhiteSpace(tenantId))
                {
                    tenantId = _options.DefaultTenantId;
                    defaultTenantIdUsed = !string.IsNullOrWhiteSpace(tenantId);
                }

                if (meetingInfo is JoinMeetingIdMeetingInfo && string.IsNullOrWhiteSpace(tenantId))
                {
                    throw new TeamsMeetingJoinException("missing_tenant_id", "tenantId is required for teams.microsoft.com/meet URL. Set DECISCOPE_DEFAULT_TENANT_ID or pass tenantId.");
                }

                _logger.LogInformation(
                    "Teams meeting URL parsed. UrlKind={UrlKind}; Format={Format}; Redirected={Redirected}; ParsedUrlSource={ParsedUrlSource}; ResolvedContextTenantIdUsed={ResolvedContextTenantIdUsed}; DefaultTenantIdUsed={DefaultTenantIdUsed}",
                    GetUrlKind(meetingInfo),
                    meetingInfo.GetType().Name,
                    redirected,
                    parsedUrlSource,
                    resolvedContextTenantIdUsed,
                    defaultTenantIdUsed);

                return new TeamsMeetingJoinInfo(chatInfo, meetingInfo, tenantId, resolvedUrl, redirected, defaultTenantIdUsed);
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

        private (ChatInfo ChatInfo, MeetingInfo MeetingInfo, string ParsedUrlSource) ParseJoinUrlWithFallback(
            Uri resolvedUrl,
            string? requestedUrl,
            string? tenantId)
        {
            try
            {
                var (chatInfo, meetingInfo) = JoinInfo.ParseJoinURL(resolvedUrl.ToString(), tenantId);
                return (chatInfo, meetingInfo, "resolved");
            }
            catch (ArgumentException ex) when (ShouldRetryOriginalJoinUrl(resolvedUrl, requestedUrl))
            {
                _logger.LogInformation(
                    ex,
                    "Resolved Teams meeting URL could not be parsed. Retrying original join URL. OriginalUrlHash={OriginalUrlHash}; CanonicalJoinWebUrlHash={CanonicalJoinWebUrlHash}",
                    HashForLog(requestedUrl),
                    HashForLog(resolvedUrl.ToString()));
                var (chatInfo, meetingInfo) = JoinInfo.ParseJoinURL(requestedUrl!, tenantId);
                return (chatInfo, meetingInfo, "original");
            }
        }

        private static bool ShouldRetryOriginalJoinUrl(Uri resolvedUrl, string? requestedUrl)
        {
            return !string.IsNullOrWhiteSpace(requestedUrl)
                && !string.Equals(requestedUrl.Trim(), resolvedUrl.ToString(), StringComparison.OrdinalIgnoreCase);
        }

        private static string GetUrlKind(MeetingInfo meetingInfo)
        {
            return meetingInfo is JoinMeetingIdMeetingInfo ? "MeetShortUrl" : "MeetupJoinUrl";
        }

        private static bool IsShortMeetingUrl(string? joinUrl)
        {
            if (string.IsNullOrWhiteSpace(joinUrl) ||
                !Uri.TryCreate(joinUrl.Trim(), UriKind.Absolute, out var uri))
            {
                return false;
            }

            var segments = uri.AbsolutePath
                .Split('/', StringSplitOptions.RemoveEmptyEntries);
            return IsTeamsHost(uri.Host)
                && segments.Length >= 2
                && segments[0].Equals("meet", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsTeamsHost(string host)
        {
            return host.Equals("teams.microsoft.com", StringComparison.OrdinalIgnoreCase)
                || host.EndsWith(".teams.microsoft.com", StringComparison.OrdinalIgnoreCase);
        }

        private static (string? Tid, string? Oid) ExtractContextIdentifiers(Uri uri)
        {
            var query = ParseQuery(uri.Query);
            if (!query.TryGetValue("context", out var context) || string.IsNullOrWhiteSpace(context))
            {
                return (null, null);
            }

            try
            {
                using var document = JsonDocument.Parse(WebUtility.UrlDecode(context));
                return (
                    GetJsonString(document.RootElement, "Tid", "tid"),
                    GetJsonString(document.RootElement, "Oid", "oid"));
            }
            catch (JsonException)
            {
                return (null, null);
            }
        }

        private static string? GetJsonString(JsonElement root, params string[] names)
        {
            foreach (var name in names)
            {
                if (root.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.String)
                {
                    return property.GetString();
                }
            }

            return null;
        }

        private static Dictionary<string, string> ParseQuery(string query)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var pair in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var parts = pair.Split('=', 2);
                var key = WebUtility.UrlDecode(parts[0]);
                if (!string.IsNullOrWhiteSpace(key))
                {
                    result[key] = parts.Length == 2 ? WebUtility.UrlDecode(parts[1]) : string.Empty;
                }
            }

            return result;
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
}
