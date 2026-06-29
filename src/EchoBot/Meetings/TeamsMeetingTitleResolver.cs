using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using EchoBot.Authentication;
using Microsoft.Extensions.Options;
using Microsoft.Identity.Client;

namespace EchoBot.Meetings
{
    public sealed class TeamsMeetingTitleResolver : ITeamsMeetingTitleResolver
    {
        public const string HttpClientName = "TeamsMeetingTitleResolver";

        private const string CandidateKindAadObjectId = "aad_object_id";
        private const string CandidateKindUserPrincipalName = "user_principal_name";
        private static readonly string[] GraphScopes = { "https://graph.microsoft.com/.default" };
        private static readonly char[] LookupUserIdSeparators = { ',', ';', ' ', '\r', '\n', '\t' };
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
            var joinContext = ExtractJoinContext(request.OriginalJoinUrl, request.ResolvedJoinUrl);
            var tenantId = FirstNonEmpty(request.TenantId, joinContext.Tid);
            var contextOrganizerId = joinContext.Oid;
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
                    OrganizerId = FirstNonEmpty(request.OrganizerId, contextOrganizerId),
                    TitleResolvedAt = DateTimeOffset.UtcNow,
                };
            }

            if (string.IsNullOrWhiteSpace(tenantId))
            {
                return Failure(request, "tenant_missing", "tenant id is required to acquire a Microsoft Graph app token");
            }
            if (string.IsNullOrWhiteSpace(settings.AadAppId) || string.IsNullOrWhiteSpace(settings.AadAppSecret))
            {
                return Failure(request, "graph_auth_config_missing", "AadAppId/AadAppSecret are required for Microsoft Graph title resolution");
            }

            string accessToken;
            try
            {
                accessToken = await AcquireTokenAsync(tenantId, cancellationToken).ConfigureAwait(false);
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
                    MeetingTenantValidator.Suffix(tenantId),
                    ex.Message);
                return Failure(request, "graph_token_acquire_failed", ex.Message);
            }

            var candidateResolution = await ResolveCandidateUsersAsync(
                request,
                accessToken,
                contextOrganizerId,
                cancellationToken).ConfigureAwait(false);
            var candidateUsers = candidateResolution.CandidateUsers;
            TeamsMeetingTitleResolutionResult? bestFailure = candidateResolution.BestFailure;
            if (candidateUsers.Count == 0)
            {
                if (bestFailure != null && !string.IsNullOrWhiteSpace(bestFailure.ErrorCode))
                {
                    return bestFailure;
                }

                LogAttempt(
                    request,
                    "candidate_users",
                    "not_found",
                    "OrganizerId, context.Oid, DefaultMeetingOrganizerUserId, CandidateUserIds, CandidateUserPrincipalNames, CandidateUserEmails, and MeetingTitleLookupUserIds were empty");
                return Failure(request, "no_candidate_users", "candidate user is required for /users/{id}/onlineMeetings lookup");
            }

            var attempted = false;
            foreach (var candidateUser in candidateUsers)
            {
                LogAttempt(
                    request,
                    "candidate_user",
                    "selected",
                    $"CandidateKind={candidateUser.Kind}; CandidateUserObjectIdHash={HashForLog(candidateUser.ObjectId)}; CandidateUserPrincipalNameHash={HashForLog(candidateUser.UserPrincipalName)}; Source={candidateUser.Source}");

                if (!string.IsNullOrWhiteSpace(request.JoinMeetingId))
                {
                    attempted = true;
                    var byJoinMeetingId = await TryOnlineMeetingLookupAsync(
                        request,
                        accessToken,
                        "graph_online_meeting_by_joinMeetingId",
                        OnlineMeetingsByJoinMeetingIdPath(candidateUser.ObjectId, request.JoinMeetingId!),
                        candidateUser,
                        cancellationToken).ConfigureAwait(false);
                    if (byJoinMeetingId.HasTitle)
                    {
                        return byJoinMeetingId;
                    }
                    bestFailure = PreferMoreActionableFailure(bestFailure, byJoinMeetingId);
                }

                if (!string.IsNullOrWhiteSpace(joinUrl))
                {
                    foreach (var joinWebUrlPropertyName in new[] { "JoinWebUrl", "joinWebUrl" })
                    {
                        attempted = true;
                        var byJoinUrl = await TryOnlineMeetingLookupAsync(
                            request,
                            accessToken,
                            "graph_online_meeting_by_joinWebUrl",
                            OnlineMeetingsByJoinWebUrlPath(candidateUser.ObjectId, joinUrl, joinWebUrlPropertyName),
                            candidateUser,
                            cancellationToken).ConfigureAwait(false);
                        if (byJoinUrl.HasTitle)
                        {
                            return byJoinUrl;
                        }
                        bestFailure = PreferMoreActionableFailure(bestFailure, byJoinUrl);
                    }
                }

                if (!string.IsNullOrWhiteSpace(joinUrl))
                {
                    attempted = true;
                    var byCalendarEvent = await TryCalendarEventLookupAsync(
                        request,
                        accessToken,
                        joinUrl,
                        candidateUser,
                        cancellationToken).ConfigureAwait(false);
                    if (byCalendarEvent.HasTitle)
                    {
                        return byCalendarEvent;
                    }
                    bestFailure = PreferMoreActionableFailure(bestFailure, byCalendarEvent);
                }
            }

            if (bestFailure != null && !string.IsNullOrWhiteSpace(bestFailure.ErrorCode))
            {
                return bestFailure;
            }

            if (!attempted)
            {
                return Failure(request, "meeting_title_lookup_skipped", "No Graph onlineMeeting/calendar lookup was attempted");
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
            CandidateUser candidateUser,
            CancellationToken cancellationToken)
        {
            LogAttempt(
                request,
                method,
                "started",
                $"CandidateKind={candidateUser.Kind}; CandidateUserObjectIdHash={HashForLog(candidateUser.ObjectId)}; JoinMeetingId={request.JoinMeetingId}");
            var graph = await GetGraphJsonAsync(
                accessToken,
                path,
                method,
                candidateUser.ObjectId,
                GraphRequestKind.OnlineMeeting,
                cancellationToken).ConfigureAwait(false);
            if (!graph.Success)
            {
                LogAttempt(request, method, "failed", $"ErrorCode={graph.ErrorCode}; StatusCode={(int?)graph.StatusCode}");
                return FailureForCandidate(
                    request,
                    candidateUser,
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
                    OrganizerId = FirstNonEmpty(request.OrganizerId, candidateUser.ObjectId),
                };
            }

            var title = JsonString(meeting.Value, "subject");
            if (string.IsNullOrWhiteSpace(title))
            {
                return FailureForCandidate(request, candidateUser, "graph_online_meeting_subject_empty", "onlineMeeting was found but subject was empty");
            }

            var organizer = OnlineMeetingOrganizer(meeting.Value);
            var organizerId = FirstNonEmpty(organizer.Id, request.OrganizerId, candidateUser.ObjectId);

            logger.LogInformation(
                "Meeting title resolution succeeded. Method={Method}; SessionId={SessionId}; Title={Title}; TitleSource={TitleSource}; CandidateKind={CandidateKind}; CandidateUserObjectIdHash={CandidateUserObjectIdHash}; OrganizerId={OrganizerId}; JoinMeetingId={JoinMeetingId}; JoinUrlHash={JoinUrlHash}",
                method,
                request.SessionId,
                title,
                "graph_online_meeting",
                candidateUser.Kind,
                HashForLog(candidateUser.ObjectId),
                organizerId,
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
                OrganizerId = organizerId,
                OrganizerName = organizer.Name,
                OrganizerEmail = organizer.Email,
                ScheduledStartAt = JsonDateTime(meeting.Value, "startDateTime"),
                ScheduledEndAt = JsonDateTime(meeting.Value, "endDateTime"),
                TitleResolvedAt = DateTimeOffset.UtcNow,
            };
        }

        private async Task<TeamsMeetingTitleResolutionResult> TryCalendarEventLookupAsync(
            TeamsMeetingTitleResolutionRequest request,
            string accessToken,
            string joinUrl,
            CandidateUser candidateUser,
            CancellationToken cancellationToken)
        {
            const string method = "calendar_event";
            LogAttempt(request, method, "started", $"CandidateKind={candidateUser.Kind}; CandidateUserObjectIdHash={HashForLog(candidateUser.ObjectId)}");
            var graph = await GetGraphJsonAsync(
                accessToken,
                CalendarEventByJoinUrlPath(candidateUser.ObjectId, joinUrl),
                method,
                candidateUser.ObjectId,
                GraphRequestKind.CalendarEvent,
                cancellationToken).ConfigureAwait(false);
            if (!graph.Success)
            {
                LogAttempt(request, method, "failed", $"ErrorCode={graph.ErrorCode}; StatusCode={(int?)graph.StatusCode}");
                return FailureForCandidate(
                    request,
                    candidateUser,
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
                    OrganizerId = FirstNonEmpty(request.OrganizerId, candidateUser.ObjectId),
                };
            }

            var title = JsonString(calendarEvent.Value, "subject");
            if (string.IsNullOrWhiteSpace(title))
            {
                return FailureForCandidate(request, candidateUser, "graph_calendar_event_subject_empty", "calendar event was found but subject was empty");
            }

            var organizer = calendarEvent.Value.TryGetProperty("organizer", out var organizerElement)
                && organizerElement.TryGetProperty("emailAddress", out var emailAddress)
                ? emailAddress
                : default;

            logger.LogInformation(
                "Meeting title resolution succeeded. Method={Method}; SessionId={SessionId}; Title={Title}; TitleSource={TitleSource}; CandidateKind={CandidateKind}; CandidateUserObjectIdHash={CandidateUserObjectIdHash}; OrganizerId={OrganizerId}; JoinUrlHash={JoinUrlHash}",
                method,
                request.SessionId,
                title,
                "graph_calendar_event",
                candidateUser.Kind,
                HashForLog(candidateUser.ObjectId),
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
                OrganizerId = FirstNonEmpty(request.OrganizerId, candidateUser.ObjectId),
                OrganizerName = organizer.ValueKind == JsonValueKind.Object ? JsonString(organizer, "name") : null,
                OrganizerEmail = organizer.ValueKind == JsonValueKind.Object ? JsonString(organizer, "address") : null,
                ScheduledStartAt = JsonNestedDateTime(calendarEvent.Value, "start"),
                ScheduledEndAt = JsonNestedDateTime(calendarEvent.Value, "end"),
                TitleResolvedAt = DateTimeOffset.UtcNow,
            };
        }

        private async Task<GraphJsonResult> GetGraphJsonAsync(
            string accessToken,
            string path,
            string method,
            string candidateIdentifier,
            GraphRequestKind requestKind,
            CancellationToken cancellationToken)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(new Uri("https://graph.microsoft.com"), path));
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            var client = httpClientFactory.CreateClient(HttpClientName);
            using var response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                var errorCode = ClassifyGraphError(response.StatusCode, body, requestKind);
                logger.LogWarning(
                    "Meeting title resolution attempt failed. Method={Method}; GraphRequestKind={GraphRequestKind}; CandidateIdentifierHash={CandidateIdentifierHash}; StatusCode={StatusCode}; ErrorCode={ErrorCode}; GraphBody={GraphBody}",
                    method,
                    requestKind,
                    HashForLog(candidateIdentifier),
                    (int)response.StatusCode,
                    errorCode,
                    Truncate(body, 1200));
                return new GraphJsonResult(false, null, errorCode, body, response.StatusCode);
            }

            try
            {
                return new GraphJsonResult(true, JsonDocument.Parse(body), null, null, response.StatusCode);
            }
            catch (JsonException ex)
            {
                return new GraphJsonResult(false, null, "graph_invalid_json", ex.Message, response.StatusCode);
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

        private TeamsMeetingTitleResolutionResult FailureForCandidate(
            TeamsMeetingTitleResolutionRequest request,
            CandidateUser candidateUser,
            string errorCode,
            string errorMessage)
        {
            logger.LogWarning(
                "Meeting title resolution attempt failed. SessionId={SessionId}; Reason={Reason}; ErrorCode={ErrorCode}; JoinUrlHash={JoinUrlHash}; CandidateKind={CandidateKind}; CandidateUserObjectIdHash={CandidateUserObjectIdHash}; CandidateUserPrincipalNameHash={CandidateUserPrincipalNameHash}; CandidateSource={CandidateSource}; OrganizerId={OrganizerId}; JoinMeetingId={JoinMeetingId}; ThreadId={ThreadId}; Stage={Stage}",
                request.SessionId,
                errorMessage,
                errorCode,
                request.JoinUrlHash,
                candidateUser.Kind,
                HashForLog(candidateUser.ObjectId),
                HashForLog(candidateUser.UserPrincipalName),
                candidateUser.Source,
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
                OrganizerId = FirstNonEmpty(request.OrganizerId, candidateUser.ObjectId),
                ErrorCode = errorCode,
                ErrorMessage = errorMessage,
            };
        }

        private async Task<CandidateResolutionResult> ResolveCandidateUsersAsync(
            TeamsMeetingTitleResolutionRequest request,
            string accessToken,
            string? contextOrganizerId,
            CancellationToken cancellationToken)
        {
            var rawCandidates = RawCandidateUsers(request, contextOrganizerId).ToArray();
            var candidateUsers = new List<CandidateUser>();
            var seenObjectIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            TeamsMeetingTitleResolutionResult? bestFailure = null;

            foreach (var rawCandidate in rawCandidates)
            {
                if (rawCandidate.Kind == CandidateKindAadObjectId)
                {
                    var candidateUser = new CandidateUser(
                        rawCandidate.Value,
                        CandidateKindAadObjectId,
                        rawCandidate.Source,
                        null,
                        null,
                        rawCandidate.Kind,
                        rawCandidate.Value);
                    if (seenObjectIds.Add(candidateUser.ObjectId))
                    {
                        LogCandidatePrepared(request, candidateUser);
                        candidateUsers.Add(candidateUser);
                    }
                    continue;
                }

                var resolved = await ResolveCandidateUserAsync(
                    request,
                    accessToken,
                    rawCandidate,
                    cancellationToken).ConfigureAwait(false);
                if (resolved.CandidateUser != null)
                {
                    if (seenObjectIds.Add(resolved.CandidateUser.ObjectId))
                    {
                        LogCandidatePrepared(request, resolved.CandidateUser);
                        candidateUsers.Add(resolved.CandidateUser);
                    }
                    continue;
                }

                if (resolved.Failure != null)
                {
                    bestFailure = PreferMoreActionableFailure(bestFailure, resolved.Failure);
                }
            }

            return new CandidateResolutionResult(candidateUsers, bestFailure);
        }

        private async Task<CandidateUserResolution> ResolveCandidateUserAsync(
            TeamsMeetingTitleResolutionRequest request,
            string accessToken,
            RawCandidateUser rawCandidate,
            CancellationToken cancellationToken)
        {
            LogAttempt(
                request,
                "graph_user_resolve",
                "started",
                $"InputKind={rawCandidate.Kind}; InputHash={HashForLog(rawCandidate.Value)}; Source={rawCandidate.Source}");

            var graph = await GetGraphJsonAsync(
                accessToken,
                UserByIdentifierPath(rawCandidate.Value),
                "graph_user_resolve",
                rawCandidate.Value,
                GraphRequestKind.UserResolve,
                cancellationToken).ConfigureAwait(false);
            if (!graph.Success)
            {
                LogAttempt(request, "graph_user_resolve", "failed", $"ErrorCode={graph.ErrorCode}; StatusCode={(int?)graph.StatusCode}; InputKind={rawCandidate.Kind}; Source={rawCandidate.Source}");
                return new CandidateUserResolution(
                    null,
                    FailureForCandidateIdentifier(
                        request,
                        rawCandidate,
                        graph.ErrorCode ?? "candidate_user_resolve_failed",
                        graph.ErrorMessage ?? "Graph user resolution failed"));
            }

            if (graph.Json == null)
            {
                return new CandidateUserResolution(
                    null,
                    FailureForCandidateIdentifier(request, rawCandidate, "candidate_user_resolve_empty", "Graph user resolution returned an empty response"));
            }

            var user = graph.Json.RootElement;
            var objectId = JsonString(user, "id");
            if (string.IsNullOrWhiteSpace(objectId))
            {
                return new CandidateUserResolution(
                    null,
                    FailureForCandidateIdentifier(request, rawCandidate, "candidate_user_id_empty", "Graph user resolution returned no id"));
            }

            var principalName = JsonString(user, "userPrincipalName");
            var mail = JsonString(user, "mail");
            var candidateUser = new CandidateUser(
                objectId.Trim(),
                CandidateKindAadObjectId,
                rawCandidate.Source,
                principalName,
                mail,
                rawCandidate.Kind,
                rawCandidate.Value);

            logger.LogInformation(
                "Graph user resolved. SessionId={SessionId}; InputKind={InputKind}; InputHash={InputHash}; CandidateKind={CandidateKind}; CandidateUserObjectIdHash={CandidateUserObjectIdHash}; UserPrincipalNameHash={UserPrincipalNameHash}; MailHash={MailHash}; Source={Source}",
                request.SessionId,
                rawCandidate.Kind,
                HashForLog(rawCandidate.Value),
                candidateUser.Kind,
                HashForLog(candidateUser.ObjectId),
                HashForLog(candidateUser.UserPrincipalName),
                HashForLog(candidateUser.Mail),
                rawCandidate.Source);

            return new CandidateUserResolution(candidateUser, null);
        }

        private TeamsMeetingTitleResolutionResult FailureForCandidateIdentifier(
            TeamsMeetingTitleResolutionRequest request,
            RawCandidateUser rawCandidate,
            string errorCode,
            string errorMessage)
        {
            logger.LogWarning(
                "Meeting title candidate resolution failed. SessionId={SessionId}; Reason={Reason}; ErrorCode={ErrorCode}; JoinUrlHash={JoinUrlHash}; InputKind={InputKind}; InputHash={InputHash}; CandidateSource={CandidateSource}; JoinMeetingId={JoinMeetingId}; ThreadId={ThreadId}; Stage={Stage}",
                request.SessionId,
                errorMessage,
                errorCode,
                request.JoinUrlHash,
                rawCandidate.Kind,
                HashForLog(rawCandidate.Value),
                rawCandidate.Source,
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

        private IEnumerable<RawCandidateUser> RawCandidateUsers(TeamsMeetingTitleResolutionRequest request, string? contextOrganizerId)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var candidate in new[]
            {
                RawCandidateFromIdentifier(request.OrganizerId, "request.organizer_id"),
                RawCandidateFromIdentifier(contextOrganizerId, "join_url_context.oid"),
                RawCandidateFromIdentifier(settings.DefaultMeetingOrganizerUserId, "settings.default_meeting_organizer_user_id"),
            })
            {
                if (TryAddRawCandidate(candidate, seen, out var rawCandidate))
                {
                    yield return rawCandidate;
                }
            }

            if (request.CandidateUserIds != null)
            {
                foreach (var value in request.CandidateUserIds)
                {
                    var candidate = RawCandidateFromIdentifier(value, "request.candidate_user_ids");
                    if (candidate != null && candidate.Kind != CandidateKindAadObjectId)
                    {
                        logger.LogWarning(
                            "Meeting title candidateUserIds contained a non-object-id value; resolving as principal name. SessionId={SessionId}; InputHash={InputHash}; InputKind={InputKind}; Source={Source}",
                            request.SessionId,
                            HashForLog(candidate.Value),
                            candidate.Kind,
                            candidate.Source);
                    }
                    if (TryAddRawCandidate(candidate, seen, out var rawCandidate))
                    {
                        yield return rawCandidate;
                    }
                }
            }

            if (request.CandidateUserPrincipalNames != null)
            {
                foreach (var value in request.CandidateUserPrincipalNames)
                {
                    if (TryAddRawCandidate(RawCandidateFromPrincipalName(value, "request.candidate_user_principal_names"), seen, out var rawCandidate))
                    {
                        yield return rawCandidate;
                    }
                }
            }

            if (request.CandidateUserEmails != null)
            {
                foreach (var value in request.CandidateUserEmails)
                {
                    if (TryAddRawCandidate(RawCandidateFromPrincipalName(value, "request.candidate_user_emails"), seen, out var rawCandidate))
                    {
                        yield return rawCandidate;
                    }
                }
            }

            foreach (var value in SplitLookupUserIds(settings.MeetingTitleLookupUserIds))
            {
                if (TryAddRawCandidate(RawCandidateFromIdentifier(value, "settings.meeting_title_lookup_user_ids"), seen, out var rawCandidate))
                {
                    yield return rawCandidate;
                }
            }
        }

        private void LogCandidatePrepared(TeamsMeetingTitleResolutionRequest request, CandidateUser candidateUser)
        {
            logger.LogInformation(
                "Meeting title candidate user prepared. SessionId={SessionId}; CandidateKind={CandidateKind}; CandidateUserObjectIdHash={CandidateUserObjectIdHash}; CandidateUserPrincipalNameHash={CandidateUserPrincipalNameHash}; CandidateSource={CandidateSource}; ResolvedFromKind={ResolvedFromKind}",
                request.SessionId,
                candidateUser.Kind,
                HashForLog(candidateUser.ObjectId),
                HashForLog(candidateUser.UserPrincipalName),
                candidateUser.Source,
                candidateUser.OriginalInputKind);
        }

        private static bool TryAddRawCandidate(RawCandidateUser? candidate, HashSet<string> seen, out RawCandidateUser rawCandidate)
        {
            rawCandidate = new RawCandidateUser(string.Empty, string.Empty, string.Empty);
            if (candidate == null)
            {
                return false;
            }

            var key = $"{candidate.Kind}:{candidate.Value}";
            if (!seen.Add(key))
            {
                return false;
            }

            rawCandidate = candidate;
            return true;
        }

        private static RawCandidateUser? RawCandidateFromIdentifier(string? value, string source)
        {
            var trimmed = value?.Trim();
            if (string.IsNullOrWhiteSpace(trimmed))
            {
                return null;
            }

            var kind = IsAadObjectId(trimmed) ? CandidateKindAadObjectId : CandidateKindUserPrincipalName;
            return new RawCandidateUser(trimmed, kind, source);
        }

        private static RawCandidateUser? RawCandidateFromPrincipalName(string? value, string source)
        {
            var trimmed = value?.Trim();
            return string.IsNullOrWhiteSpace(trimmed)
                ? null
                : new RawCandidateUser(trimmed, CandidateKindUserPrincipalName, source);
        }

        private static IEnumerable<string> SplitLookupUserIds(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                yield break;
            }

            foreach (var part in value.Split(LookupUserIdSeparators, StringSplitOptions.RemoveEmptyEntries))
            {
                var trimmed = part.Trim();
                if (!string.IsNullOrWhiteSpace(trimmed))
                {
                    yield return trimmed;
                }
            }
        }

        private static TeamsMeetingTitleResolutionResult? PreferMoreActionableFailure(
            TeamsMeetingTitleResolutionResult? current,
            TeamsMeetingTitleResolutionResult candidate)
        {
            if (candidate.HasTitle || string.IsNullOrWhiteSpace(candidate.ErrorCode))
            {
                return current;
            }
            if (current == null || FailurePriority(candidate.ErrorCode) > FailurePriority(current.ErrorCode))
            {
                return candidate;
            }
            return current;
        }

        private static int FailurePriority(string? errorCode)
        {
            return errorCode switch
            {
                "permission_missing" => 90,
                "admin_consent_missing" => 85,
                "application_access_policy_missing" => 80,
                "user_not_allowed_by_policy" => 75,
                "online_meeting_access_denied" => 72,
                "access_policy_missing" => 70,
                "user_resolve_admin_consent_missing" => 69,
                "user_resolve_permission_missing" => 68,
                "user_resolve_access_denied" => 67,
                "candidate_user_not_found" => 65,
                "meeting_not_found" => 60,
                "calendar_permission_missing" => 55,
                "calendar_admin_consent_missing" => 55,
                "calendar_access_denied" => 55,
                "graph_query_not_supported" => 50,
                "candidate_user_query_not_supported" => 50,
                "calendar_query_not_supported" => 45,
                "graph_online_meeting_subject_empty" => 40,
                "graph_calendar_event_subject_empty" => 40,
                "graph_request_failed" => 30,
                _ => 10,
            };
        }

        private void LogAttempt(TeamsMeetingTitleResolutionRequest request, string method, string result, string? detail)
        {
            request.Attempts?.Add(new TeamsMeetingTitleResolutionAttempt
            {
                Method = method,
                Result = result,
                Detail = detail,
            });

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

        private static string OnlineMeetingsByJoinWebUrlPath(string organizerId, string joinWebUrl, string propertyName)
        {
            var filter = $"{propertyName} eq '{EscapeODataString(joinWebUrl)}'";
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

        private static string UserByIdentifierPath(string identifier)
        {
            return $"/v1.0/users/{Uri.EscapeDataString(identifier)}?$select=id,userPrincipalName,mail";
        }

        private static string EscapeODataString(string value) => value.Replace("'", "''", StringComparison.Ordinal);

        private static string ClassifyGraphError(HttpStatusCode statusCode, string body, GraphRequestKind requestKind)
        {
            return statusCode switch
            {
                HttpStatusCode.Unauthorized => requestKind switch
                {
                    GraphRequestKind.CalendarEvent => "calendar_permission_missing",
                    GraphRequestKind.UserResolve => "user_resolve_permission_missing",
                    _ => "permission_missing",
                },
                HttpStatusCode.Forbidden => requestKind switch
                {
                    GraphRequestKind.CalendarEvent => ClassifyCalendarForbiddenGraphError(body),
                    GraphRequestKind.UserResolve => ClassifyUserResolveForbiddenGraphError(body),
                    _ => ClassifyOnlineMeetingForbiddenGraphError(body),
                },
                HttpStatusCode.NotFound => requestKind switch
                {
                    GraphRequestKind.UserResolve => "candidate_user_not_found",
                    GraphRequestKind.CalendarEvent => "calendar_event_not_found",
                    _ => "meeting_not_found",
                },
                HttpStatusCode.BadRequest => requestKind switch
                {
                    GraphRequestKind.UserResolve => "candidate_user_query_not_supported",
                    GraphRequestKind.CalendarEvent => "calendar_query_not_supported",
                    _ => "graph_query_not_supported",
                },
                _ => "graph_request_failed",
            };
        }

        private static string ClassifyOnlineMeetingForbiddenGraphError(string body)
        {
            var normalized = body.ToLowerInvariant();
            if (normalized.Contains("applicationaccesspolicy", StringComparison.Ordinal)
                || normalized.Contains("application access policy", StringComparison.Ordinal))
            {
                return "application_access_policy_missing";
            }
            if (normalized.Contains("admin consent", StringComparison.Ordinal)
                || normalized.Contains("consent", StringComparison.Ordinal))
            {
                return "admin_consent_missing";
            }
            if (normalized.Contains("not allowed", StringComparison.Ordinal)
                || normalized.Contains("not authorized", StringComparison.Ordinal))
            {
                return "user_not_allowed_by_policy";
            }
            if (normalized.Contains("permission", StringComparison.Ordinal)
                || normalized.Contains("privileges", StringComparison.Ordinal))
            {
                return "permission_missing";
            }
            return "online_meeting_access_denied";
        }

        private static string ClassifyCalendarForbiddenGraphError(string body)
        {
            var normalized = body.ToLowerInvariant();
            if (normalized.Contains("admin consent", StringComparison.Ordinal)
                || normalized.Contains("consent", StringComparison.Ordinal))
            {
                return "calendar_admin_consent_missing";
            }
            if (normalized.Contains("permission", StringComparison.Ordinal)
                || normalized.Contains("privileges", StringComparison.Ordinal)
                || normalized.Contains("accessdenied", StringComparison.Ordinal)
                || normalized.Contains("access denied", StringComparison.Ordinal))
            {
                return "calendar_permission_missing";
            }
            return "calendar_access_denied";
        }

        private static string ClassifyUserResolveForbiddenGraphError(string body)
        {
            var normalized = body.ToLowerInvariant();
            if (normalized.Contains("admin consent", StringComparison.Ordinal)
                || normalized.Contains("consent", StringComparison.Ordinal))
            {
                return "user_resolve_admin_consent_missing";
            }
            if (normalized.Contains("permission", StringComparison.Ordinal)
                || normalized.Contains("privileges", StringComparison.Ordinal)
                || normalized.Contains("accessdenied", StringComparison.Ordinal)
                || normalized.Contains("access denied", StringComparison.Ordinal))
            {
                return "user_resolve_permission_missing";
            }
            return "user_resolve_access_denied";
        }

        private static JoinUrlContext ExtractJoinContext(string? originalJoinUrl, string? resolvedJoinUrl)
        {
            foreach (var uri in CandidateJoinUris(originalJoinUrl, resolvedJoinUrl))
            {
                var query = ParseQuery(uri.Query);
                if (!query.TryGetValue("context", out var context) || string.IsNullOrWhiteSpace(context))
                {
                    continue;
                }

                try
                {
                    using var document = JsonDocument.Parse(WebUtility.UrlDecode(context));
                    return new JoinUrlContext(
                        JsonStringAnyCase(document.RootElement, "Tid", "tid"),
                        JsonStringAnyCase(document.RootElement, "Oid", "oid"));
                }
                catch (JsonException)
                {
                }
            }

            return new JoinUrlContext(null, null);
        }

        private static (string? Id, string? Name, string? Email) OnlineMeetingOrganizer(JsonElement meeting)
        {
            if (!meeting.TryGetProperty("participants", out var participants)
                || !participants.TryGetProperty("organizer", out var organizer))
            {
                return (null, null, null);
            }

            var name = JsonString(organizer, "displayName");
            var email = JsonString(organizer, "upn");
            if (organizer.TryGetProperty("identity", out var identity)
                && identity.TryGetProperty("user", out var user))
            {
                return (
                    JsonString(user, "id"),
                    FirstNonEmpty(JsonString(user, "displayName"), name),
                    FirstNonEmpty(JsonString(user, "userPrincipalName"), email));
            }

            return (null, name, email);
        }

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

        private static string? JsonStringAnyCase(JsonElement element, params string[] names)
        {
            foreach (var name in names)
            {
                var value = JsonString(element, name);
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value;
                }
            }

            return null;
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

        private static string HashForLog(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value.Trim()));
            return Convert.ToHexString(bytes, 0, 8);
        }

        private static bool IsAadObjectId(string? value)
        {
            return Guid.TryParse(value?.Trim(), out _);
        }

        private enum GraphRequestKind
        {
            OnlineMeeting,
            CalendarEvent,
            UserResolve,
        }

        private sealed record GraphJsonResult(
            bool Success,
            JsonDocument? Json,
            string? ErrorCode,
            string? ErrorMessage,
            HttpStatusCode? StatusCode);

        private sealed record RawCandidateUser(string Value, string Kind, string Source);

        private sealed record CandidateUser(
            string ObjectId,
            string Kind,
            string Source,
            string? UserPrincipalName,
            string? Mail,
            string OriginalInputKind,
            string OriginalInput);

        private sealed record CandidateUserResolution(CandidateUser? CandidateUser, TeamsMeetingTitleResolutionResult? Failure);

        private sealed record CandidateResolutionResult(
            IReadOnlyList<CandidateUser> CandidateUsers,
            TeamsMeetingTitleResolutionResult? BestFailure);

        private sealed record JoinUrlContext(string? Tid, string? Oid);
    }
}
