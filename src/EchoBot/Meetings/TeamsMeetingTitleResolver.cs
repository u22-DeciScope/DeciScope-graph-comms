using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using EchoBot.Authentication;
using Microsoft.Extensions.Options;
using Microsoft.Identity.Client;

namespace EchoBot.Meetings
{
    public sealed class TeamsMeetingTitleResolver : ITeamsMeetingTitleResolver
    {
        public const string HttpClientName = "TeamsMeetingTitleResolver";

        private static readonly string[] GraphScopes = { "https://graph.microsoft.com/.default" };
        private readonly IHttpClientFactory httpClientFactory;
        private readonly AppSettings settings;
        private readonly ILogger<TeamsMeetingTitleResolver> logger;

        public TeamsMeetingTitleResolver(
            IHttpClientFactory httpClientFactory,
            IOptions<AppSettings> settings,
            ILogger<TeamsMeetingTitleResolver> logger)
        {
            this.httpClientFactory = httpClientFactory;
            this.settings = settings.Value;
            this.logger = logger;
        }

        public async Task<TeamsMeetingTitleResolutionResult> ResolveAsync(
            TeamsMeetingTitleResolutionRequest request,
            CancellationToken cancellationToken)
        {
            var joinUrl = FirstNonEmpty(request.ResolvedJoinUrl, request.OriginalJoinUrl);
            var urlTitle = TryResolveTitleFromJoinUrl(request.OriginalJoinUrl, request.ResolvedJoinUrl);
            LogAttempt(request, "join_url_metadata", urlTitle.Title != null ? "succeeded" : "not_found", null);
            if (!string.IsNullOrWhiteSpace(urlTitle.Title))
            {
                return new TeamsMeetingTitleResolutionResult
                {
                    Title = urlTitle.Title,
                    TitleSource = "teams_metadata",
                    Provider = "teams",
                    ExternalMeetingId = request.JoinMeetingId,
                    JoinMeetingId = request.JoinMeetingId,
                    JoinWebUrl = joinUrl,
                    CanonicalJoinWebUrl = request.ResolvedJoinUrl,
                    ThreadId = request.ThreadId,
                    OrganizerId = request.OrganizerId,
                    TitleResolvedAt = DateTimeOffset.UtcNow,
                };
            }

            if (string.IsNullOrWhiteSpace(request.TenantId))
            {
                return Failure(request, "tenant_missing", "tenant id is required to acquire a Microsoft Graph app token");
            }
            if (string.IsNullOrWhiteSpace(settings.AadAppId) || string.IsNullOrWhiteSpace(settings.AadAppSecret))
            {
                return Failure(request, "graph_auth_config_missing", "AadAppId/AadAppSecret are required for Microsoft Graph title resolution");
            }
            if (string.IsNullOrWhiteSpace(request.OrganizerId))
            {
                return Failure(request, "organizer_unknown", "organizer user id is required for /users/{id}/onlineMeetings lookup");
            }

            string accessToken;
            try
            {
                accessToken = await AcquireTokenAsync(request.TenantId, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                logger.LogWarning(
                    ex,
                    "Meeting title resolution failed. Method=graph_token; SessionId={SessionId}; Reason={Reason}; ErrorCode={ErrorCode}; JoinUrlHash={JoinUrlHash}; TenantSuffix={TenantSuffix}; Error={Error}",
                    request.SessionId,
                    "failed to acquire graph token",
                    "graph_token_acquire_failed",
                    request.JoinUrlHash,
                    MeetingTenantValidator.Suffix(request.TenantId),
                    ex.Message);
                return Failure(request, "graph_token_acquire_failed", ex.Message);
            }

            if (!string.IsNullOrWhiteSpace(joinUrl))
            {
                var byJoinUrl = await TryOnlineMeetingLookupAsync(
                    request,
                    accessToken,
                    "joinWebUrl",
                    OnlineMeetingsByJoinWebUrlPath(request.OrganizerId!, joinUrl),
                    cancellationToken).ConfigureAwait(false);
                if (byJoinUrl.HasTitle || !string.IsNullOrWhiteSpace(byJoinUrl.ErrorCode))
                {
                    return byJoinUrl;
                }
            }

            if (!string.IsNullOrWhiteSpace(request.JoinMeetingId))
            {
                var byJoinMeetingId = await TryOnlineMeetingLookupAsync(
                    request,
                    accessToken,
                    "joinMeetingId",
                    OnlineMeetingsByJoinMeetingIdPath(request.OrganizerId!, request.JoinMeetingId!),
                    cancellationToken).ConfigureAwait(false);
                if (byJoinMeetingId.HasTitle || !string.IsNullOrWhiteSpace(byJoinMeetingId.ErrorCode))
                {
                    return byJoinMeetingId;
                }
            }

            if (!string.IsNullOrWhiteSpace(joinUrl))
            {
                var byCalendarEvent = await TryCalendarEventLookupAsync(
                    request,
                    accessToken,
                    joinUrl,
                    cancellationToken).ConfigureAwait(false);
                if (byCalendarEvent.HasTitle || !string.IsNullOrWhiteSpace(byCalendarEvent.ErrorCode))
                {
                    return byCalendarEvent;
                }
            }

            return Failure(request, "meeting_title_not_found", "Graph onlineMeeting/calendar lookup returned no matching title");
        }

        private async Task<string> AcquireTokenAsync(string tenantId, CancellationToken cancellationToken)
        {
            var app = ConfidentialClientApplicationBuilder
                .Create(settings.AadAppId)
                .WithAuthority(AuthenticationProvider.BuildAuthority(tenantId))
                .WithClientSecret(settings.AadAppSecret)
                .Build();

            var result = await app.AcquireTokenForClient(GraphScopes)
                .ExecuteAsync(cancellationToken)
                .ConfigureAwait(false);
            return result.AccessToken;
        }

        private async Task<TeamsMeetingTitleResolutionResult> TryOnlineMeetingLookupAsync(
            TeamsMeetingTitleResolutionRequest request,
            string accessToken,
            string method,
            string path,
            CancellationToken cancellationToken)
        {
            LogAttempt(request, method, "started", null);
            var graph = await GetGraphJsonAsync(accessToken, path, cancellationToken).ConfigureAwait(false);
            if (!graph.Success)
            {
                return Failure(
                    request,
                    graph.ErrorCode ?? "graph_online_meeting_lookup_failed",
                    graph.ErrorMessage ?? "Graph onlineMeeting lookup failed");
            }

            var meeting = FirstObjectFromValueArray(graph.Json);
            if (meeting == null)
            {
                LogAttempt(request, method, "not_found", null);
                return new TeamsMeetingTitleResolutionResult
                {
                    Provider = "teams",
                    ExternalMeetingId = request.JoinMeetingId,
                    JoinMeetingId = request.JoinMeetingId,
                    JoinWebUrl = FirstNonEmpty(request.ResolvedJoinUrl, request.OriginalJoinUrl),
                    CanonicalJoinWebUrl = request.ResolvedJoinUrl,
                    ThreadId = request.ThreadId,
                    OrganizerId = request.OrganizerId,
                };
            }

            var title = JsonString(meeting.Value, "subject");
            if (string.IsNullOrWhiteSpace(title))
            {
                return Failure(request, "graph_online_meeting_subject_empty", "onlineMeeting was found but subject was empty");
            }

            logger.LogInformation(
                "Meeting title resolution succeeded. Method={Method}; SessionId={SessionId}; Title={Title}; TitleSource={TitleSource}; OrganizerId={OrganizerId}; JoinMeetingId={JoinMeetingId}; JoinUrlHash={JoinUrlHash}",
                method,
                request.SessionId,
                title,
                "graph_online_meeting",
                request.OrganizerId,
                request.JoinMeetingId,
                request.JoinUrlHash);

            return new TeamsMeetingTitleResolutionResult
            {
                Title = title,
                TitleSource = "graph_online_meeting",
                Provider = "teams",
                ExternalMeetingId = FirstNonEmpty(JsonString(meeting.Value, "id"), request.JoinMeetingId),
                JoinMeetingId = request.JoinMeetingId,
                JoinWebUrl = FirstNonEmpty(JsonString(meeting.Value, "joinWebUrl"), request.ResolvedJoinUrl, request.OriginalJoinUrl),
                CanonicalJoinWebUrl = FirstNonEmpty(JsonString(meeting.Value, "joinWebUrl"), request.ResolvedJoinUrl),
                ThreadId = request.ThreadId,
                OrganizerId = request.OrganizerId,
                ScheduledStartAt = JsonDateTime(meeting.Value, "startDateTime"),
                ScheduledEndAt = JsonDateTime(meeting.Value, "endDateTime"),
                TitleResolvedAt = DateTimeOffset.UtcNow,
            };
        }

        private async Task<TeamsMeetingTitleResolutionResult> TryCalendarEventLookupAsync(
            TeamsMeetingTitleResolutionRequest request,
            string accessToken,
            string joinUrl,
            CancellationToken cancellationToken)
        {
            const string method = "calendar_event";
            LogAttempt(request, method, "started", null);
            var graph = await GetGraphJsonAsync(
                accessToken,
                CalendarEventByJoinUrlPath(request.OrganizerId!, joinUrl),
                cancellationToken).ConfigureAwait(false);
            if (!graph.Success)
            {
                return Failure(
                    request,
                    graph.ErrorCode ?? "graph_calendar_event_lookup_failed",
                    graph.ErrorMessage ?? "Graph calendar event lookup failed");
            }

            var calendarEvent = FirstObjectFromValueArray(graph.Json);
            if (calendarEvent == null)
            {
                LogAttempt(request, method, "not_found", null);
                return new TeamsMeetingTitleResolutionResult
                {
                    Provider = "teams",
                    ExternalMeetingId = request.JoinMeetingId,
                    JoinMeetingId = request.JoinMeetingId,
                    JoinWebUrl = joinUrl,
                    CanonicalJoinWebUrl = request.ResolvedJoinUrl,
                    ThreadId = request.ThreadId,
                    OrganizerId = request.OrganizerId,
                };
            }

            var title = JsonString(calendarEvent.Value, "subject");
            if (string.IsNullOrWhiteSpace(title))
            {
                return Failure(request, "graph_calendar_event_subject_empty", "calendar event was found but subject was empty");
            }

            var organizer = calendarEvent.Value.TryGetProperty("organizer", out var organizerElement)
                && organizerElement.TryGetProperty("emailAddress", out var emailAddress)
                ? emailAddress
                : default;

            logger.LogInformation(
                "Meeting title resolution succeeded. Method={Method}; SessionId={SessionId}; Title={Title}; TitleSource={TitleSource}; OrganizerId={OrganizerId}; JoinUrlHash={JoinUrlHash}",
                method,
                request.SessionId,
                title,
                "graph_calendar_event",
                request.OrganizerId,
                request.JoinUrlHash);

            return new TeamsMeetingTitleResolutionResult
            {
                Title = title,
                TitleSource = "graph_calendar_event",
                Provider = "teams",
                ExternalMeetingId = FirstNonEmpty(JsonString(calendarEvent.Value, "id"), request.JoinMeetingId),
                JoinMeetingId = request.JoinMeetingId,
                JoinWebUrl = joinUrl,
                CanonicalJoinWebUrl = request.ResolvedJoinUrl,
                ThreadId = request.ThreadId,
                OrganizerId = request.OrganizerId,
                OrganizerName = organizer.ValueKind == JsonValueKind.Object ? JsonString(organizer, "name") : null,
                OrganizerEmail = organizer.ValueKind == JsonValueKind.Object ? JsonString(organizer, "address") : null,
                ScheduledStartAt = JsonNestedDateTime(calendarEvent.Value, "start"),
                ScheduledEndAt = JsonNestedDateTime(calendarEvent.Value, "end"),
                TitleResolvedAt = DateTimeOffset.UtcNow,
            };
        }

        private async Task<GraphJsonResult> GetGraphJsonAsync(string accessToken, string path, CancellationToken cancellationToken)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(new Uri("https://graph.microsoft.com"), path));
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            var client = httpClientFactory.CreateClient(HttpClientName);
            using var response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                var errorCode = response.StatusCode switch
                {
                    HttpStatusCode.Unauthorized => "permission_missing",
                    HttpStatusCode.Forbidden => "access_policy_missing",
                    HttpStatusCode.NotFound => "graph_resource_not_found",
                    HttpStatusCode.BadRequest => "graph_query_not_supported",
                    _ => "graph_request_failed",
                };
                logger.LogWarning(
                    "Meeting title resolution attempt failed. Method=graph_http; StatusCode={StatusCode}; ErrorCode={ErrorCode}; GraphBody={GraphBody}",
                    (int)response.StatusCode,
                    errorCode,
                    Truncate(body, 1200));
                return new GraphJsonResult(false, null, errorCode, body);
            }

            try
            {
                return new GraphJsonResult(true, JsonDocument.Parse(body), null, null);
            }
            catch (JsonException ex)
            {
                return new GraphJsonResult(false, null, "graph_invalid_json", ex.Message);
            }
        }

        private TeamsMeetingTitleResolutionResult Failure(TeamsMeetingTitleResolutionRequest request, string errorCode, string errorMessage)
        {
            logger.LogWarning(
                "Meeting title resolution failed. SessionId={SessionId}; Reason={Reason}; ErrorCode={ErrorCode}; JoinUrlHash={JoinUrlHash}; OrganizerId={OrganizerId}; JoinMeetingId={JoinMeetingId}; ThreadId={ThreadId}; Stage={Stage}",
                request.SessionId,
                errorMessage,
                errorCode,
                request.JoinUrlHash,
                request.OrganizerId,
                request.JoinMeetingId,
                request.ThreadId,
                request.Stage);

            return new TeamsMeetingTitleResolutionResult
            {
                Provider = "teams",
                ExternalMeetingId = request.JoinMeetingId,
                JoinMeetingId = request.JoinMeetingId,
                JoinWebUrl = FirstNonEmpty(request.ResolvedJoinUrl, request.OriginalJoinUrl),
                CanonicalJoinWebUrl = request.ResolvedJoinUrl,
                ThreadId = request.ThreadId,
                OrganizerId = request.OrganizerId,
                ErrorCode = errorCode,
                ErrorMessage = errorMessage,
            };
        }

        private void LogAttempt(TeamsMeetingTitleResolutionRequest request, string method, string result, string? detail)
        {
            logger.LogInformation(
                "Meeting title resolution attempt. Method={Method}; Result={Result}; SessionId={SessionId}; JoinUrlHash={JoinUrlHash}; OrganizerId={OrganizerId}; JoinMeetingId={JoinMeetingId}; ThreadId={ThreadId}; Detail={Detail}",
                method,
                result,
                request.SessionId,
                request.JoinUrlHash,
                request.OrganizerId,
                request.JoinMeetingId,
                request.ThreadId,
                detail);
        }

        private static string OnlineMeetingsByJoinWebUrlPath(string organizerId, string joinWebUrl)
        {
            var filter = $"JoinWebUrl eq '{EscapeODataString(joinWebUrl)}'";
            return $"/v1.0/users/{Uri.EscapeDataString(organizerId)}/onlineMeetings?$filter={Uri.EscapeDataString(filter)}";
        }

        private static string OnlineMeetingsByJoinMeetingIdPath(string organizerId, string joinMeetingId)
        {
            var filter = $"joinMeetingIdSettings/joinMeetingId eq '{EscapeODataString(joinMeetingId)}'";
            return $"/v1.0/users/{Uri.EscapeDataString(organizerId)}/onlineMeetings?$filter={Uri.EscapeDataString(filter)}";
        }

        private static string CalendarEventByJoinUrlPath(string organizerId, string joinUrl)
        {
            var filter = $"onlineMeetingUrl eq '{EscapeODataString(joinUrl)}'";
            return $"/v1.0/users/{Uri.EscapeDataString(organizerId)}/events?$select=subject,organizer,start,end,onlineMeetingUrl,onlineMeeting&$top=5&$filter={Uri.EscapeDataString(filter)}";
        }

        private static string EscapeODataString(string value) => value.Replace("'", "''", StringComparison.Ordinal);

        private static (string? Title, string? TitleSource) TryResolveTitleFromJoinUrl(string? originalJoinUrl, string? resolvedJoinUrl)
        {
            foreach (var uri in CandidateJoinUris(originalJoinUrl, resolvedJoinUrl))
            {
                var query = ParseQuery(uri.Query);
                foreach (var key in new[] { "title", "subject", "meetingTitle", "meetingSubject", "topic" })
                {
                    if (query.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
                    {
                        return (value.Trim(), "teams_metadata");
                    }
                }

                if (query.TryGetValue("context", out var context)
                    && TryResolveTitleFromContext(context, out var contextTitle))
                {
                    return (contextTitle, "teams_metadata");
                }
            }

            return (null, null);
        }

        private static IEnumerable<Uri> CandidateJoinUris(string? originalJoinUrl, string? resolvedJoinUrl)
        {
            foreach (var value in new[] { originalJoinUrl, resolvedJoinUrl })
            {
                if (!string.IsNullOrWhiteSpace(value) && Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri))
                {
                    yield return uri;
                }
            }
        }

        private static bool TryResolveTitleFromContext(string context, out string? title)
        {
            title = null;
            try
            {
                using var document = JsonDocument.Parse(WebUtility.UrlDecode(context));
                foreach (var key in new[] { "subject", "Subject", "title", "Title", "meetingTitle", "topic" })
                {
                    if (document.RootElement.TryGetProperty(key, out var property)
                        && property.ValueKind == JsonValueKind.String)
                    {
                        title = property.GetString()?.Trim();
                        return !string.IsNullOrWhiteSpace(title);
                    }
                }
            }
            catch (JsonException)
            {
            }

            return false;
        }

        private static Dictionary<string, string> ParseQuery(string query)
        {
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var pair in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var parts = pair.Split('=', 2);
                var key = WebUtility.UrlDecode(parts[0]);
                if (!string.IsNullOrWhiteSpace(key))
                {
                    values[key] = parts.Length == 2 ? WebUtility.UrlDecode(parts[1]) : string.Empty;
                }
            }

            return values;
        }

        private static JsonElement? FirstObjectFromValueArray(JsonDocument? document)
        {
            if (document == null)
            {
                return null;
            }
            if (document.RootElement.TryGetProperty("value", out var value)
                && value.ValueKind == JsonValueKind.Array
                && value.GetArrayLength() > 0)
            {
                return value[0];
            }
            if (document.RootElement.ValueKind == JsonValueKind.Object)
            {
                return document.RootElement;
            }
            return null;
        }

        private static string? JsonString(JsonElement element, string name)
        {
            return element.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.String
                ? property.GetString()
                : null;
        }

        private static DateTimeOffset? JsonDateTime(JsonElement element, string name)
        {
            var value = JsonString(element, name);
            return DateTimeOffset.TryParse(value, out var parsed) ? parsed : null;
        }

        private static DateTimeOffset? JsonNestedDateTime(JsonElement element, string name)
        {
            if (!element.TryGetProperty(name, out var nested))
            {
                return null;
            }
            return JsonDateTime(nested, "dateTime");
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

        private static string Truncate(string value, int maxLength)
        {
            if (string.IsNullOrEmpty(value) || value.Length <= maxLength)
            {
                return value;
            }
            return value.Substring(0, maxLength);
        }

        private sealed record GraphJsonResult(bool Success, JsonDocument? Json, string? ErrorCode, string? ErrorMessage);
    }
}
