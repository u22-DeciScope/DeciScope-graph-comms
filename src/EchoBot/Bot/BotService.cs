// ***********************************************************************
// Assembly         : EchoBot.Bot
// Author           : JasonTheDeveloper
// Created          : 09-07-2020
//
// Last Modified By : bcage29
// Last Modified On : 10-17-2023
// ***********************************************************************
// <copyright file="BotService.cs" company="Microsoft">
//     Copyright ©  2023
// </copyright>
// <summary></summary>
// ***********************************************************************
using EchoBot.Authentication;
using EchoBot.Constants;
using EchoBot.Models;
using Microsoft.Extensions.Options;
using Microsoft.Graph;
using Microsoft.Graph.Communications.Calls;
using Microsoft.Graph.Communications.Calls.Media;
using Microsoft.Graph.Communications.Client;
using Microsoft.Graph.Communications.Common;
using Microsoft.Graph.Communications.Common.Telemetry;
using Microsoft.Graph.Communications.Resources;
using Microsoft.Skype.Bots.Media;
using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using EchoBot.Util;
using Microsoft.Graph.Models;
using Microsoft.Graph.Contracts;
using EchoBot.Meetings;
using EchoBot.Services;
using System.Diagnostics;
using System.Reflection;

namespace EchoBot.Bot
{
    /// <summary>
    /// Class BotService.
    /// Implements the <see cref="System.IDisposable" />
    /// Implements the <see cref="EchoBot.Bot.IBotService" />
    /// </summary>
    /// <seealso cref="System.IDisposable" />
    /// <seealso cref="EchoBot.Bot.IBotService" />
    public class BotService : IDisposable, IBotService
    {
        /// <summary>
        /// The Graph logger
        /// </summary>
        private readonly IGraphLogger _graphLogger;

        /// <summary>
        /// The logger
        /// </summary>
        private readonly ILogger _logger;

        /// <summary>
        /// The settings
        /// </summary>
        private readonly AppSettings _settings;

        /// <summary>
        /// Logger for logging media platform information
        /// </summary>
        private readonly IBotMediaLogger _mediaPlatformLogger;

        private readonly ITeamsMeetingJoinInfoProvider _joinInfoProvider;

        private readonly ITeamsMeetingTitleResolver _titleResolver;

        private readonly IMeetingTenantContext _meetingTenantContext;

        private readonly IRecordingStatusUpdater _recordingStatusUpdater;

        private readonly ITranscriptRepository _transcriptRepository;

        private readonly ITranscriptForwarder _transcriptForwarder;

        private readonly BotControlOptions _botControlOptions;

        private readonly BotMeetingSessionRegistry _sessionRegistry;

        private readonly IBotMeetingStatusReporter _statusReporter;

        private readonly IBotJoinCommandService _joinCommandService;

        private readonly PolicyRecordingCallRegistry _policyRecordingCallRegistry = new PolicyRecordingCallRegistry();

        private readonly ConcurrentDictionary<Guid, string> _pendingCommandSessionsByScenarioId = new ConcurrentDictionary<Guid, string>();

        /// <summary>
        /// Gets the collection of call handlers.
        /// </summary>
        /// <value>The call handlers.</value>
        public ConcurrentDictionary<string, CallHandler> CallHandlers { get; } = new ConcurrentDictionary<string, CallHandler>();

        /// <summary>
        /// Gets the entry point for stateful bot.
        /// </summary>
        /// <value>The client.</value>
        public ICommunicationsClient Client { get; private set; }


        /// <summary>
        /// Dispose of the call client
        /// </summary>
        public void Dispose()
        {
            this.Client?.Dispose();
            this.Client = null;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="BotService" /> class.
        /// </summary>
        /// <param name="graphLogger"></param>
        /// <param name="logger"></param>
        /// <param name="settings"></param>
        /// <param name="mediaLogger"></param>
        public BotService(
            IGraphLogger graphLogger,
            ILogger<BotService> logger,
            IOptions<AppSettings> settings,
            IBotMediaLogger mediaLogger,
            ITeamsMeetingJoinInfoProvider joinInfoProvider,
            IMeetingTenantContext meetingTenantContext,
            IRecordingStatusUpdater recordingStatusUpdater,
            ITranscriptRepository transcriptRepository,
            ITranscriptForwarder transcriptForwarder,
            BotControlOptions botControlOptions,
            BotMeetingSessionRegistry sessionRegistry,
            ITeamsMeetingTitleResolver titleResolver,
            IBotMeetingStatusReporter statusReporter,
            IBotJoinCommandService joinCommandService)
        {
            _graphLogger = graphLogger;
            _logger = logger;
            _settings = settings.Value;
            _mediaPlatformLogger = mediaLogger;
            _joinInfoProvider = joinInfoProvider;
            _titleResolver = titleResolver;
            _meetingTenantContext = meetingTenantContext;
            _recordingStatusUpdater = recordingStatusUpdater;
            _transcriptRepository = transcriptRepository;
            _transcriptForwarder = transcriptForwarder;
            _botControlOptions = botControlOptions;
            _sessionRegistry = sessionRegistry;
            _statusReporter = statusReporter;
            _joinCommandService = joinCommandService;
        }

        /// <summary>
        /// Initialize the instance.
        /// </summary>
        public void Initialize()
        {
            _logger.LogInformation("Initializing Bot Service");
            ValidatePolicyRecordingSettings();
            var name = this.GetType().Assembly.GetName().Name;
            var applicationId = _settings.AadAppId;
            var builder = new CommunicationsClientBuilder(
                name,
                applicationId,
                _graphLogger);

            var authProvider = new AuthenticationProvider(
                name,
                applicationId,
                _settings.AadAppSecret,
                _graphLogger,
                _meetingTenantContext);

            var mediaPlatformSettings = new MediaPlatformSettings()
            {
                MediaPlatformInstanceSettings = new MediaPlatformInstanceSettings()
                {
                    CertificateThumbprint = _settings.CertificateThumbprint,
                    InstanceInternalPort = _settings.MediaInternalPort,
                    InstancePublicIPAddress = IPAddress.Any,
                    InstancePublicPort = _settings.MediaInstanceExternalPort,
                    ServiceFqdn = _settings.MediaDnsName
                },
                ApplicationId = applicationId,
                MediaPlatformLogger = _mediaPlatformLogger
            };

            var notificationUrl = new Uri($"https://{_settings.ServiceDnsName}:{_settings.BotInstanceExternalPort}/{HttpRouteConstants.CallSignalingRoutePrefix}/{HttpRouteConstants.OnNotificationRequestRoute}");
            _logger.LogInformation($"NotificationUrl: ${notificationUrl}");

            builder.SetAuthenticationProvider(authProvider);
            builder.SetNotificationUrl(notificationUrl);
            builder.SetMediaPlatformSettings(mediaPlatformSettings);
            builder.SetServiceBaseUrl(new Uri(AppConstants.PlaceCallEndpointUrl));

            this.Client = builder.Build();
            this.Client.Calls().OnIncoming += this.CallsOnIncoming;
            this.Client.Calls().OnUpdated += this.CallsOnUpdated;
        }

        private void ValidatePolicyRecordingSettings()
        {
            _logger.LogInformation(
                "Policy recording configuration. Enabled={Enabled}; UseSpeechService={UseSpeechService}; ServiceDnsConfigured={ServiceDnsConfigured}; MediaPort={MediaPort}; SignalingPort={SignalingPort}",
                _settings.EnablePolicyRecording,
                _settings.UseSpeechService,
                !string.IsNullOrWhiteSpace(_settings.ServiceDnsName),
                _settings.MediaInstanceExternalPort,
                _settings.BotInstanceExternalPort);

            if (!_settings.EnablePolicyRecording)
            {
                return;
            }

            var missing = new List<string>();
            if (string.IsNullOrWhiteSpace(_settings.AadAppId)) missing.Add(nameof(_settings.AadAppId));
            if (string.IsNullOrWhiteSpace(_settings.AadAppSecret)) missing.Add(nameof(_settings.AadAppSecret));
            if (string.IsNullOrWhiteSpace(_settings.CertificateThumbprint)) missing.Add(nameof(_settings.CertificateThumbprint));
            if (string.IsNullOrWhiteSpace(_settings.ServiceDnsName)) missing.Add(nameof(_settings.ServiceDnsName));
            if (_settings.BotInstanceExternalPort <= 0) missing.Add(nameof(_settings.BotInstanceExternalPort));
            if (_settings.MediaInstanceExternalPort <= 0) missing.Add(nameof(_settings.MediaInstanceExternalPort));
            if (_settings.MediaInternalPort <= 0) missing.Add(nameof(_settings.MediaInternalPort));

            if (_settings.UseSpeechService)
            {
                if (string.IsNullOrWhiteSpace(_settings.SpeechKey) && string.IsNullOrWhiteSpace(_settings.SpeechConfigKey))
                {
                    missing.Add(nameof(_settings.SpeechKey));
                }

                if (string.IsNullOrWhiteSpace(_settings.SpeechRegion) && string.IsNullOrWhiteSpace(_settings.SpeechConfigRegion))
                {
                    missing.Add(nameof(_settings.SpeechRegion));
                }
            }

            if (missing.Count > 0)
            {
                var message = $"Policy recording is enabled, but required settings are missing: {string.Join(", ", missing)}.";
                _logger.LogError(message);
                throw new InvalidOperationException(message);
            }
        }

        /// <summary>
        /// Terminate all calls before and dispose of client
        /// </summary>
        /// <returns></returns>
        public async Task Shutdown()
        {
            _logger.LogWarning("Terminating all calls during shutdown event");
            var shutdownTasks = this.CallHandlers.Values
                .Distinct()
                .Select(handler => handler.ShutdownAsync())
                .ToArray();

            if (shutdownTasks.Length > 0)
            {
                var allShutdowns = Task.WhenAll(shutdownTasks);
                var completed = await Task.WhenAny(allShutdowns, Task.Delay(TimeSpan.FromSeconds(5))).ConfigureAwait(false);
                if (completed != allShutdowns)
                {
                    _logger.LogWarning("Timed out waiting for call shutdown status reports. CallCount={CallCount}", shutdownTasks.Length);
                }
            }

            await this.Client.TerminateAsync();
            this.Dispose();
        }

        /// <summary>
        /// End a particular call.
        /// </summary>
        /// <param name="threadId">The call thread id.</param>
        /// <returns>The <see cref="Task" />.</returns>
        public async Task EndCallByThreadIdAsync(string threadId)
        {
            string callId = string.Empty;
            try
            {
                var callHandler = this.GetHandlerOrThrow(threadId);
                callId = callHandler.Call.Id;
                await callHandler.Call.DeleteAsync().ConfigureAwait(false);
            }
            catch (Exception)
            {
                // Manually remove the call from SDK state.
                // This will trigger the ICallCollection.OnUpdated event with the removed resource.
                if (!string.IsNullOrEmpty(callId))
                {
                    this.Client.Calls().TryForceRemove(callId, out ICall _);
                }
            }
        }

        /// <summary>
        /// Joins the call asynchronously.
        /// </summary>
        /// <param name="joinCallBody">The join call body.</param>
        /// <returns>The <see cref="ICall" /> that was requested to join.</returns>
        public async Task<ICall> JoinCallAsync(JoinCallBody joinCallBody, CancellationToken cancellationToken = default)
        {
            return await JoinCallCoreAsync(joinCallBody, null, CallOrigin.OutboundJoin, cancellationToken).ConfigureAwait(false);
        }

        public async Task<ICall> JoinMeetingAsync(
            string sessionId,
            string joinUrl,
            string? tenantId = null,
            IReadOnlyCollection<string>? candidateUserIds = null,
            string? joinMeetingId = null,
            string? canonicalJoinWebUrl = null,
            IReadOnlyCollection<string>? candidateUserPrincipalNames = null,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(sessionId))
            {
                throw new ArgumentException("sessionId is required.", nameof(sessionId));
            }

            if (string.IsNullOrWhiteSpace(joinUrl))
            {
                throw new ArgumentException("joinUrl is required.", nameof(joinUrl));
            }

            return await JoinCallCoreAsync(
                new JoinCallBody
                {
                    JoinUrl = joinUrl,
                    TenantId = tenantId,
                    CandidateUserIds = candidateUserIds,
                    CandidateUserPrincipalNames = candidateUserPrincipalNames,
                    JoinMeetingId = joinMeetingId,
                    CanonicalJoinWebUrl = canonicalJoinWebUrl,
                },
                sessionId,
                CallOrigin.CommandJoin,
                cancellationToken).ConfigureAwait(false);
        }

        private async Task<ICall> JoinCallCoreAsync(
            JoinCallBody joinCallBody,
            string? sessionId,
            CallOrigin origin,
            CancellationToken cancellationToken = default)
        {
            // A tracking id for logging purposes. Helps identify this call in logs.
            var scenarioId = Guid.NewGuid();
            if (!string.IsNullOrWhiteSpace(sessionId))
            {
                _pendingCommandSessionsByScenarioId[scenarioId] = sessionId;
            }

            var joinInfo = await _joinInfoProvider.GetJoinInfoAsync(joinCallBody, cancellationToken).ConfigureAwait(false);
            await ResolveAndReportMeetingMetadataAsync(
                sessionId,
                joinCallBody,
                joinInfo,
                threadIdOverride: null,
                stage: "before_graph_join",
                cancellationToken).ConfigureAwait(false);

            var applicationId = _settings.AadAppId;
            var meetingTenantId = MeetingTenantValidator.NormalizeAndValidate(joinInfo.TenantId, applicationId);

            _logger.LogInformation(
                "Starting Graph meeting join request. ScenarioId={ScenarioId}; Format={Format}; Redirected={Redirected}; TenantIdSuffix={TenantIdSuffix}; AppIdSuffix={AppIdSuffix}; SameValue={SameValue}",
                scenarioId,
                joinInfo.MeetingInfo.GetType().Name,
                joinInfo.Redirected,
                MeetingTenantValidator.Suffix(meetingTenantId),
                MeetingTenantValidator.Suffix(applicationId),
                string.Equals(meetingTenantId, applicationId, StringComparison.OrdinalIgnoreCase));

            var mediaSession = this.CreateLocalMediaSession();

            var joinParams = new JoinMeetingParameters(joinInfo.ChatInfo, joinInfo.MeetingInfo, mediaSession)
            {
                TenantId = meetingTenantId,
                IsParticipantInfoUpdatesEnabled = true,
            };

            if (!string.IsNullOrWhiteSpace(joinCallBody.DisplayName))
            {
                // Teams client does not allow changing of ones own display name.
                // If display name is specified, we join as anonymous (guest) user
                // with the specified display name.  This will put bot into lobby
                // unless lobby bypass is disabled.
                joinParams.GuestIdentity = new Identity
                {
                    Id = Guid.NewGuid().ToString(),
                    DisplayName = joinCallBody.DisplayName,
                };
            }

            // For short meeting URL joins, ChatInfo.ThreadId is not known until after the call
            // is established, so skip duplicate detection when ThreadId is null or empty.
            var threadId = joinParams.ChatInfo.ThreadId;
            if (!string.IsNullOrEmpty(threadId) && this.CallHandlers.TryGetValue(threadId, out CallHandler? _))
            {
                throw new Exception("Call has already been added");
            }

            using var tenantScope = _meetingTenantContext.UseMeetingTenant(meetingTenantId);
            ICall statefulCall;
            try
            {
                statefulCall = await this.Client.Calls().AddAsync(joinParams, scenarioId, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                if (string.IsNullOrWhiteSpace(sessionId))
                {
                    _pendingCommandSessionsByScenarioId.TryRemove(scenarioId, out _);
                }
            }
            if (!string.IsNullOrWhiteSpace(sessionId))
            {
                _sessionRegistry.Register(statefulCall.Id, sessionId, origin.ToString());
                var threadKey = statefulCall.Resource?.ChatInfo?.ThreadId;
                if (!string.IsNullOrWhiteSpace(threadKey))
                {
                    _sessionRegistry.Register(threadKey, sessionId, origin.ToString(), primaryCallId: false);
                }

                PromoteExistingHandlerToCommandJoin(statefulCall, sessionId);
                _pendingCommandSessionsByScenarioId.TryRemove(scenarioId, out _);
            }

            statefulCall.GraphLogger.Info($"Call creation complete: {statefulCall.Id}");
            _logger.LogInformation(
                "Graph meeting join request accepted. ScenarioId={ScenarioId}; CallId={CallId}; SessionId={SessionId}; Origin={Origin}",
                scenarioId,
                statefulCall.Id,
                sessionId,
                origin);
            await ResolveAndReportMeetingMetadataAsync(
                sessionId,
                joinCallBody,
                joinInfo,
                statefulCall.Resource?.ChatInfo?.ThreadId,
                "after_graph_join",
                cancellationToken).ConfigureAwait(false);
            return statefulCall;
        }

        private async Task ResolveAndReportMeetingMetadataAsync(
            string? sessionId,
            JoinCallBody joinCallBody,
            TeamsMeetingJoinInfo joinInfo,
            string? threadIdOverride,
            string stage,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(sessionId))
            {
                return;
            }

            var joinUrl = joinInfo.ResolvedJoinUrl.ToString();
            var joinUrlHash = ComputeJoinUrlHash(joinUrl);
            var threadId = FirstNonEmpty(threadIdOverride, joinInfo.ChatInfo.ThreadId);
            var joinMeetingId = FirstNonEmpty(joinCallBody.JoinMeetingId, ExtractExternalMeetingId(joinInfo.MeetingInfo));
            var organizerId = ExtractOrganizerId(joinInfo.MeetingInfo);
            var candidateUserIds = CandidateUserIdsFromJoinBody(joinCallBody);
            var candidateUserPrincipalNames = CandidateUserPrincipalNamesFromJoinBody(joinCallBody);
            var availableIdentifiers = new
            {
                Format = joinInfo.MeetingInfo.GetType().Name,
                ThreadId = threadId,
                JoinMeetingId = joinMeetingId,
                OrganizerId = organizerId,
                CandidateUserIdsCount = candidateUserIds.Count,
                CandidateUserIdsHash = HashesForLog(candidateUserIds),
                CandidateUserPrincipalNamesCount = candidateUserPrincipalNames.Count,
                CandidateUserPrincipalNamesHash = HashesForLog(candidateUserPrincipalNames),
                joinInfo.Redirected,
                joinInfo.DefaultTenantIdUsed,
                Stage = stage,
            };

            _logger.LogInformation(
                "Meeting title resolution started. SessionId={SessionId}; Stage={Stage}; JoinUrlHash={JoinUrlHash}; AvailableIdentifiers={AvailableIdentifiers}",
                sessionId,
                stage,
                joinUrlHash,
                availableIdentifiers);

            var titleResult = await _titleResolver.ResolveAsync(
                new TeamsMeetingTitleResolutionRequest
                {
                    SessionId = sessionId,
                    TenantId = joinInfo.TenantId,
                    OriginalJoinUrl = joinCallBody.GetMeetingUrl(),
                    ResolvedJoinUrl = FirstNonEmpty(joinCallBody.CanonicalJoinWebUrl, joinInfo.ResolvedJoinUrl.ToString()),
                    JoinUrlHash = joinUrlHash,
                    ThreadId = threadId,
                    JoinMeetingId = joinMeetingId,
                    OrganizerId = organizerId,
                    CandidateUserIds = candidateUserIds,
                    CandidateUserPrincipalNames = candidateUserPrincipalNames,
                    Stage = stage,
                },
                cancellationToken).ConfigureAwait(false);

            if (titleResult.HasTitle)
            {
                _logger.LogInformation(
                    "Meeting title resolution succeeded. SessionId={SessionId}; Title={Title}; TitleSource={TitleSource}; OrganizerId={OrganizerId}; ScheduledStartAt={ScheduledStartAt}; ScheduledEndAt={ScheduledEndAt}; JoinUrlHash={JoinUrlHash}; AvailableIdentifiers={AvailableIdentifiers}",
                    sessionId,
                    titleResult.Title,
                    titleResult.TitleSource,
                    titleResult.OrganizerId,
                    titleResult.ScheduledStartAt,
                    titleResult.ScheduledEndAt,
                    joinUrlHash,
                    availableIdentifiers);
            }
            else
            {
                _logger.LogWarning(
                    "Meeting title resolution failed. SessionId={SessionId}; Reason={Reason}; ErrorCode={ErrorCode}; JoinUrlHash={JoinUrlHash}; AvailableIdentifiers={AvailableIdentifiers}",
                    sessionId,
                    titleResult.ErrorMessage,
                    titleResult.ErrorCode,
                    joinUrlHash,
                    availableIdentifiers);
            }

            if (string.IsNullOrWhiteSpace(titleResult.Title)
                && string.IsNullOrWhiteSpace(threadId)
                && string.IsNullOrWhiteSpace(joinMeetingId)
                && string.IsNullOrWhiteSpace(titleResult.ErrorCode))
            {
                return;
            }

            _logger.LogInformation(
                "Meeting metadata report started. SessionId={SessionId}; Title={Title}; TitleSource={TitleSource}; Provider={Provider}; ThreadId={ThreadId}; ExternalMeetingId={ExternalMeetingId}; JoinMeetingId={JoinMeetingId}; OrganizerId={OrganizerId}; ErrorCode={ErrorCode}; JoinUrlHash={JoinUrlHash}",
                sessionId,
                titleResult.Title,
                titleResult.TitleSource,
                titleResult.Provider,
                titleResult.ThreadId ?? threadId,
                titleResult.ExternalMeetingId,
                titleResult.JoinMeetingId ?? joinMeetingId,
                titleResult.OrganizerId ?? organizerId,
                titleResult.ErrorCode,
                joinUrlHash);

            await _statusReporter.ReportMetadataAsync(
                sessionId,
                new BotMeetingMetadataUpdate(
                    titleResult.Title,
                    titleResult.TitleSource,
                    titleResult.Provider,
                    titleResult.ExternalMeetingId,
                    titleResult.JoinMeetingId ?? joinMeetingId,
                    titleResult.JoinWebUrl,
                    titleResult.CanonicalJoinWebUrl,
                    titleResult.ThreadId ?? threadId,
                    titleResult.OrganizerId ?? organizerId,
                    titleResult.OrganizerName,
                    titleResult.OrganizerEmail,
                    titleResult.ScheduledStartAt,
                    titleResult.ScheduledEndAt,
                    titleResult.ErrorCode,
                    titleResult.ErrorMessage,
                    titleResult.TitleResolvedAt),
                cancellationToken).ConfigureAwait(false);
        }

        private static (string? Title, string? TitleSource) TryResolveTitleFromJoinUrl(string? originalJoinUrl, Uri resolvedJoinUrl)
        {
            foreach (var uri in CandidateJoinUris(originalJoinUrl, resolvedJoinUrl))
            {
                var query = ParseQuery(uri.Query);
                foreach (var key in new[] { "title", "subject", "meetingTitle", "meetingSubject", "topic" })
                {
                    if (query.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
                    {
                        return (value.Trim(), "join_url_query");
                    }
                }

                if (query.TryGetValue("context", out var context)
                    && TryResolveTitleFromContext(context, out var contextTitle))
                {
                    return (contextTitle, "join_url_context");
                }
            }

            return (null, null);
        }

        private static IEnumerable<Uri> CandidateJoinUris(string? originalJoinUrl, Uri resolvedJoinUrl)
        {
            if (!string.IsNullOrWhiteSpace(originalJoinUrl)
                && Uri.TryCreate(originalJoinUrl.Trim(), UriKind.Absolute, out var originalUri))
            {
                yield return originalUri;
            }

            yield return resolvedJoinUrl;
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
                if (string.IsNullOrWhiteSpace(key))
                {
                    continue;
                }
                values[key] = parts.Length == 2 ? WebUtility.UrlDecode(parts[1]) : string.Empty;
            }
            return values;
        }

        private static string? ExtractExternalMeetingId(MeetingInfo meetingInfo)
        {
            return meetingInfo is JoinMeetingIdMeetingInfo joinMeetingId
                ? joinMeetingId.JoinMeetingId
                : null;
        }

        private static string? ExtractOrganizerId(MeetingInfo meetingInfo)
        {
            return meetingInfo is OrganizerMeetingInfo organizerMeetingInfo
                ? organizerMeetingInfo.Organizer?.User?.Id
                : null;
        }

        private static IReadOnlyCollection<string> CandidateUserIdsFromJoinBody(JoinCallBody joinCallBody)
        {
            if (joinCallBody.CandidateUserIds == null)
            {
                return Array.Empty<string>();
            }

            return UniqueTrimmed(joinCallBody.CandidateUserIds);
        }

        private static IReadOnlyCollection<string> CandidateUserPrincipalNamesFromJoinBody(JoinCallBody joinCallBody)
        {
            if (joinCallBody.CandidateUserPrincipalNames == null)
            {
                return Array.Empty<string>();
            }

            return UniqueTrimmed(joinCallBody.CandidateUserPrincipalNames);
        }

        private static IReadOnlyCollection<string> UniqueTrimmed(IEnumerable<string> values)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var result = new List<string>();
            foreach (var value in values)
            {
                var trimmed = value?.Trim();
                if (string.IsNullOrWhiteSpace(trimmed) || !seen.Add(trimmed))
                {
                    continue;
                }
                result.Add(trimmed);
            }
            return result;
        }

        private static string? FirstNonEmpty(params string?[] values)
        {
            foreach (var value in values)
            {
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value;
                }
            }
            return null;
        }

        private static string ComputeJoinUrlHash(string joinUrl)
        {
            var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(joinUrl.Trim()));
            return Convert.ToHexString(bytes).Substring(0, 16);
        }

        private static string HashesForLog(IEnumerable<string>? values)
        {
            if (values == null)
            {
                return "[]";
            }

            var hashes = values
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => ComputeJoinUrlHash(value))
                .ToArray();
            return hashes.Length == 0 ? "[]" : $"[{string.Join(",", hashes)}]";
        }

        private void PromoteExistingHandlerToCommandJoin(ICall call, string sessionId)
        {
            var promoted = false;
            CallHandler? callIdHandler = null;
            if (!string.IsNullOrWhiteSpace(call.Id)
                && this.CallHandlers.TryGetValue(call.Id, out callIdHandler))
            {
                callIdHandler.ApplyCommandContext(sessionId);
                promoted = true;
            }

            var threadId = call.Resource?.ChatInfo?.ThreadId;
            if (!string.IsNullOrWhiteSpace(threadId)
                && this.CallHandlers.TryGetValue(threadId, out var threadHandler)
                && !ReferenceEquals(threadHandler, callIdHandler))
            {
                threadHandler.ApplyCommandContext(sessionId);
                promoted = true;
            }

            _logger.LogInformation(
                "Command join call context registered. CallId={CallId}; ThreadId={ThreadId}; SessionId={SessionId}; ExistingHandlerPromoted={ExistingHandlerPromoted}",
                call.Id,
                threadId,
                sessionId,
                promoted);
        }

        /// <summary>
        /// Creates the local media session.
        /// </summary>
        /// <param name="mediaSessionId">The media session identifier.
        /// This should be a unique value for each call.</param>
        /// <returns>The <see cref="ILocalMediaSession" />.</returns>
        private ILocalMediaSession CreateLocalMediaSession(Guid mediaSessionId = default)
        {
            try
            {
                // create media session object, this is needed to establish call connections
                return this.Client.CreateMediaSession(
                    new AudioSocketSettings
                    {
                        StreamDirections = StreamDirection.Sendrecv,
                        // Note! Currently, the only audio format supported when receiving unmixed audio is Pcm16K
                        SupportedAudioFormat = AudioFormat.Pcm16K,
                        ReceiveUnmixedMeetingAudio = true
                    },
                    new VideoSocketSettings
                    {
                        StreamDirections = StreamDirection.Inactive
                    },
                    mediaSessionId: mediaSessionId);
            }
            catch (Exception e)
            {
                _logger.LogError(e.Message);
                throw;
            }
        }

        /// <summary>
        /// Incoming call handler.
        /// </summary>
        /// <param name="sender">The sender.</param>
        /// <param name="args">The <see cref="CollectionEventArgs{TResource}" /> instance containing the event data.</param>
        private void CallsOnIncoming(ICallCollection sender, CollectionEventArgs<ICall> args)
        {
            foreach (var call in args.AddedResources)
            {
                _ = this.HandlePolicyRecordingIncomingCallAsync(call);
            }
        }

        private async Task HandlePolicyRecordingIncomingCallAsync(ICall call)
        {
            var receivedAt = DateTimeOffset.UtcNow;
            var stopwatch = Stopwatch.StartNew();
            var callId = call.Id ?? string.Empty;

            _logger.LogInformation(
                "Policy recording incoming call received. CallId={CallId}; ReceivedAt={ReceivedAt}; Direction={Direction}; State={State}; HasIncomingContext={HasIncomingContext}",
                callId,
                receivedAt,
                call.Resource?.Direction,
                call.Resource?.State,
                call.Resource?.IncomingContext != null);

            if (!_settings.EnablePolicyRecording)
            {
                _logger.LogInformation("Incoming call ignored. CallId={CallId}; Reason=PolicyRecordingDisabled", callId);
                return;
            }

            if (!_botControlOptions.AutoUserTriggerEnabled)
            {
                _logger.LogInformation(
                    "Incoming call ignored. CallId={CallId}; Reason=AutoUserTriggerDisabled; JoinMode={JoinMode}",
                    callId,
                    _botControlOptions.JoinMode);
                return;
            }

            if (!PolicyRecordingCallClassifier.IsPolicyRecordingIncoming(call.Resource))
            {
                _logger.LogInformation("Incoming call ignored. CallId={CallId}; Reason=NotPolicyRecording", callId);
                return;
            }

            if (this.CallHandlers.ContainsKey(callId) || !this._policyRecordingCallRegistry.TryReserve(callId))
            {
                _logger.LogInformation("Incoming call ignored. CallId={CallId}; Reason=AlreadyHandled", callId);
                return;
            }

            _logger.LogInformation("Policy recording incoming call accepted for processing. CallId={CallId}", callId);
            ILocalMediaSession? localMediaSession = null;
            CallHandler? callHandler = null;
            try
            {
                _logger.LogInformation("Creating policy recording media session. CallId={CallId}", callId);
                localMediaSession = Guid.TryParse(callId, out var mediaSessionId)
                    ? this.CreateLocalMediaSession(mediaSessionId)
                    : this.CreateLocalMediaSession();

                _logger.LogInformation(
                    "Policy recording media session created. CallId={CallId}; ElapsedMs={ElapsedMs}",
                    callId,
                    stopwatch.ElapsedMilliseconds);

                callHandler = new CallHandler(
                    call,
                    _settings,
                    _logger,
                    _recordingStatusUpdater,
                    _transcriptRepository,
                    _transcriptForwarder,
                    _statusReporter,
                    CallOrigin.PolicyRecordingIncoming,
                    null,
                    localMediaSession);

                if (!this.CallHandlers.TryAdd(callId, callHandler))
                {
                    _logger.LogInformation("Incoming call ignored. CallId={CallId}; Reason=AlreadyHandled", callId);
                    this._policyRecordingCallRegistry.Release(callId);
                    callHandler.Dispose();
                    return;
                }

                _logger.LogInformation("Answering policy recording call. CallId={CallId}", callId);
                await call.AnswerAsync(localMediaSession).ConfigureAwait(false);

                stopwatch.Stop();
                _logger.LogInformation(
                    "Policy recording call answered. CallId={CallId}; ElapsedMs={ElapsedMs}",
                    callId,
                    stopwatch.ElapsedMilliseconds);

                if (PolicyRecordingCallClassifier.ShouldWarnAnswerLatency(stopwatch.ElapsedMilliseconds, _settings.PolicyRecordingAnswerWarningMs))
                {
                    _logger.LogWarning(
                        "Policy recording call answer was slow. CallId={CallId}; ElapsedMs={ElapsedMs}; WarningThresholdMs={WarningThresholdMs}",
                        callId,
                        stopwatch.ElapsedMilliseconds,
                        _settings.PolicyRecordingAnswerWarningMs);
                }
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                _logger.LogError(
                    ex,
                    "Policy recording call answer failed. CallId={CallId}; ExceptionType={ExceptionType}; StatusCode={StatusCode}; GraphSubcode={GraphSubcode}; ElapsedMs={ElapsedMs}",
                    callId,
                    ex.GetType().Name,
                    GetExceptionProperty(ex, "StatusCode"),
                    GetExceptionProperty(ex, "ErrorCode"),
                    stopwatch.ElapsedMilliseconds);

                if (!string.IsNullOrEmpty(callId) && this.CallHandlers.TryRemove(callId, out var registeredHandler))
                {
                    this._policyRecordingCallRegistry.Release(callId);
                    await registeredHandler.ShutdownAsync().ConfigureAwait(false);
                    registeredHandler.Dispose();
                }
                else
                {
                    callHandler?.Dispose();
                }
            }
        }

        /// <summary>
        /// Updated call handler.
        /// </summary>
        /// <param name="sender">The <see cref="ICallCollection" /> sender.</param>
        /// <param name="args">The <see cref="CollectionEventArgs{ICall}" /> instance containing the event data.</param>
        private void CallsOnUpdated(ICallCollection sender, CollectionEventArgs<ICall> args)
        {
            foreach (var call in args.AddedResources)
            {
                if (!string.IsNullOrEmpty(call.Id) && this.CallHandlers.ContainsKey(call.Id))
                {
                    _logger.LogDebug("Call update added resource ignored because handler already exists. CallId={CallId}; Origin={Origin}", call.Id, CallOrigin.PolicyRecordingIncoming);
                    continue;
                }

                var threadId = call.Resource.ChatInfo?.ThreadId ?? call.Id;
                if (!this.CallHandlers.ContainsKey(threadId))
                {
                    var sessionId = _sessionRegistry.GetSessionId(call.Id) ?? _sessionRegistry.GetSessionId(threadId);
                    if (string.IsNullOrWhiteSpace(sessionId)
                        && call.ScenarioId != Guid.Empty
                        && _pendingCommandSessionsByScenarioId.TryGetValue(call.ScenarioId, out var pendingSessionId))
                    {
                        sessionId = pendingSessionId;
                        _logger.LogInformation(
                            "Command join session resolved from pending ScenarioId. CallId={CallId}; ThreadId={ThreadId}; ScenarioId={ScenarioId}; SessionId={SessionId}",
                            call.Id,
                            threadId,
                            call.ScenarioId,
                            sessionId);
                    }

                    if (!string.IsNullOrWhiteSpace(sessionId))
                    {
                        _sessionRegistry.Register(threadId, sessionId, CallOrigin.CommandJoin.ToString());
                        if (!string.IsNullOrWhiteSpace(call.Id))
                        {
                            _sessionRegistry.Register(call.Id, sessionId, CallOrigin.CommandJoin.ToString());
                        }
                    }

                    var origin = string.IsNullOrWhiteSpace(sessionId)
                        ? CallOrigin.OutboundJoin
                        : CallOrigin.CommandJoin;

                    var callHandler = new CallHandler(
                        call,
                        _settings,
                        _logger,
                        _recordingStatusUpdater,
                        _transcriptRepository,
                        _transcriptForwarder,
                        _statusReporter,
                        origin,
                        sessionId,
                        callEndedCallback: this.OnCallEndedAsync);
                    this.CallHandlers[threadId] = callHandler;
                }
            }

            foreach (var call in args.RemovedResources)
            {
                var threadId = call.Resource.ChatInfo?.ThreadId ?? call.Id;
                if (this.CallHandlers.TryRemove(threadId, out CallHandler? handler)
                    || (!string.IsNullOrEmpty(call.Id) && this.CallHandlers.TryRemove(call.Id, out handler)))
                {
                    this._policyRecordingCallRegistry.Release(call.Id);
                    _sessionRegistry.Remove(call.Id);
                    _sessionRegistry.Remove(threadId);
                    _ = Task.Run(async () =>
                    {
                        await handler.ShutdownAsync().ConfigureAwait(false);
                        handler.Dispose();
                    });
                }
            }
        }

        private Task OnCallEndedAsync(string? sessionId, string callId, string? reason)
        {
            _logger.LogInformation(
                "Call lifecycle ended. SessionId={SessionId}; CallId={CallId}; Reason={Reason}",
                sessionId,
                callId,
                reason);

            _joinCommandService.MarkSessionEnded(sessionId, callId, reason);
            _sessionRegistry.Remove(callId);
            return Task.CompletedTask;
        }

        private static object? GetExceptionProperty(Exception exception, string propertyName)
        {
            return exception.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance)?.GetValue(exception);
        }

        /// <summary>
        /// The get handler or throw.
        /// </summary>
        /// <param name="threadId">The call thread id.</param>
        /// <returns>The <see cref="CallHandler" />.</returns>
        /// <exception cref="ArgumentException">call ({callLegId}) not found</exception>
        private CallHandler GetHandlerOrThrow(string threadId)
        {
            if (!this.CallHandlers.TryGetValue(threadId, out CallHandler? handler))
            {
                throw new ArgumentException($"call ({threadId}) not found");
            }

            return handler;
        }
    }
}

