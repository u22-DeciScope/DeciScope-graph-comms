using EchoBot.Util;
using EchoBot.Services;
using Microsoft.Graph;
using Microsoft.Graph.Communications.Calls;
using Microsoft.Graph.Communications.Calls.Media;
using Microsoft.Graph.Communications.Common.Telemetry;
using Microsoft.Graph.Communications.Resources;
using Microsoft.Graph.Models;
using System.Timers;

namespace EchoBot.Bot
{
    /// <summary>
    /// Call Handler Logic.
    /// </summary>
    public class CallHandler : HeartbeatHandler
    {
        /// <summary>
        /// Gets the call.
        /// </summary>
        /// <value>The call.</value>
        public ICall Call { get; }

        /// <summary>
        /// Gets the bot media stream.
        /// </summary>
        /// <value>The bot media stream.</value>
        public BotMediaStream BotMediaStream { get; private set; } = null!;

        private readonly AppSettings settings;
        private readonly ILogger logger;
        private readonly IRecordingStatusUpdater recordingStatusUpdater;
        private readonly IBotMeetingStatusReporter statusReporter;
        private readonly Func<string?, string, string?, Task>? callEndedCallback;
        private CallOrigin origin;
        private string? sessionId;
        private int recordingStarted;
        private int transcriptionStartAttempted;
        private int terminationHandled;
        private CancellationTokenSource? botOnlyLeaveCts;
        private CancellationTokenSource? deciScopeHeartbeatCts;
        private PolicyRecordingCallState policyRecordingState = PolicyRecordingCallState.None;

        /// <summary>
        /// Initializes a new instance of the <see cref="CallHandler" /> class.
        /// </summary>
        /// <param name="statefulCall">The stateful call.</param>
        /// <param name="settings">The settings.</param>
        /// <param name="logger"></param>
        public CallHandler(
            ICall statefulCall,
            AppSettings settings,
            ILogger logger,
            IRecordingStatusUpdater recordingStatusUpdater,
            ITranscriptSequenceProvider transcriptSequenceProvider,
            ITranscriptForwarder transcriptForwarder,
            IBotMeetingStatusReporter statusReporter,
            CallOrigin origin = CallOrigin.OutboundJoin,
            string? sessionId = null,
            ILocalMediaSession? localMediaSession = null,
            Func<string?, string, string?, Task>? callEndedCallback = null
        )
            : base(TimeSpan.FromMinutes(10), statefulCall.GraphLogger)
        {
            this.Call = statefulCall;
            this.settings = settings;
            this.logger = logger;
            this.recordingStatusUpdater = recordingStatusUpdater;
            this.statusReporter = statusReporter;
            this.callEndedCallback = callEndedCallback;
            this.origin = origin;
            this.sessionId = sessionId;
            this.Call.OnUpdated += this.CallOnUpdated;
            this.Call.Participants.OnUpdated += this.ParticipantsOnUpdated;

            this.BotMediaStream = new BotMediaStream(localMediaSession ?? this.Call.GetLocalMediaSession(), this.Call.Id, this.GraphLogger, logger, settings, transcriptSequenceProvider, transcriptForwarder, statusReporter, sessionId, origin);

            this.logger.LogInformation(
                "CallHandler initialized. CallId={CallId}; Origin={Origin}; SessionId={SessionId}; HasMediaStream={HasMediaStream}; MediaMode={MediaMode}",
                this.Call.Id,
                this.origin,
                this.sessionId,
                this.BotMediaStream != null,
                CallDiagnostics.GetMediaMode(this.settings.UseSpeechService));

            if (this.origin == CallOrigin.PolicyRecordingIncoming)
            {
                this.logger.LogInformation("Policy recording CallHandler initialized. CallId={CallId}", this.Call.Id);
            }

            if (this.origin == CallOrigin.CommandJoin && this.Call.Resource?.State == CallState.Established)
            {
                _ = this.StartMediaAndSpeechPipelineAsync(this.Call.Id ?? string.Empty, reportRecordingStatus: false)
                    .ForgetAndLogExceptionAsync(this.GraphLogger, "Command join speech pipeline startup failed");
            }

            if (this.origin == CallOrigin.CommandJoin && this.sessionId != null)
            {
                this.StartDeciScopeHeartbeat();
            }
        }

        public void ApplyCommandContext(string sessionId)
        {
            if (string.IsNullOrWhiteSpace(sessionId))
            {
                return;
            }

            this.sessionId = sessionId;
            this.origin = CallOrigin.CommandJoin;
            this.BotMediaStream?.UpdateContext(sessionId, this.origin);
            this.StartDeciScopeHeartbeat();

            this.logger.LogInformation(
                "CallHandler context updated for command join. CallId={CallId}; SessionId={SessionId}; Origin={Origin}; State={State}",
                this.Call.Id,
                this.sessionId,
                this.origin,
                this.Call.Resource?.State);

            if (this.Call.Resource?.State == CallState.Established)
            {
                _ = this.StartMediaAndSpeechPipelineAsync(this.Call.Id ?? string.Empty, reportRecordingStatus: false)
                    .ForgetAndLogExceptionAsync(this.GraphLogger, "Command join speech pipeline startup after context update failed");
            }
        }

        /// <inheritdoc/>
        protected override Task HeartbeatAsync(ElapsedEventArgs args)
        {
            return this.Call.KeepAliveAsync();
        }

        /// <inheritdoc />
        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            this.Call.OnUpdated -= this.CallOnUpdated;
            this.Call.Participants.OnUpdated -= this.ParticipantsOnUpdated;
            this.CancelBotOnlyLeave();
            this.StopDeciScopeHeartbeat();

            _ = this.ShutdownAsync().ForgetAndLogExceptionAsync(this.GraphLogger);
        }

        public async Task ShutdownAsync()
        {
            var callId = this.Call?.Id ?? string.Empty;
            if (this.origin == CallOrigin.PolicyRecordingIncoming
                && Interlocked.CompareExchange(ref terminationHandled, 1, 0) == 0)
            {
                this.policyRecordingState = PolicyRecordingCallState.Stopping;
                this.logger.LogInformation("Stopping persistence. CallId={CallId}; Origin={Origin}", callId, this.origin);
                this.logger.LogInformation("Persistence stopped. CallId={CallId}; Origin={Origin}", callId, this.origin);
                await this.StopSpeechAndRecordingAsync(callId).ConfigureAwait(false);
            }
            else if (this.origin == CallOrigin.CommandJoin
                && Interlocked.CompareExchange(ref terminationHandled, 1, 0) == 0)
            {
                await this.HandleCommandJoinEndedAsync(callId, "shutdown").ConfigureAwait(false);
                await this.StopSpeechAndRecordingAsync(callId).ConfigureAwait(false);
            }

            if (this.BotMediaStream != null)
            {
                await this.BotMediaStream.ShutdownAsync().ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Event fired when the call has been updated.
        /// </summary>
        /// <param name="sender">The call.</param>
        /// <param name="e">The event args containing call changes.</param>
        private async void CallOnUpdated(ICall sender, ResourceEventArgs<Call> e)
        {
            var oldState = e.OldResource?.State;
            var newState = e.NewResource?.State;
            var resultInfo = e.NewResource?.ResultInfo;
            var hasMediaStream = BotMediaStream != null;
            var callId = sender?.Id ?? this.Call?.Id ?? string.Empty;

            this.logger.LogDebug(
                "Call state changed. CallId={CallId}; OldState={OldState}; NewState={NewState}; ResultCode={ResultCode}; ResultMessage={ResultMessage}; HasMediaStream={HasMediaStream}",
                callId,
                oldState,
                newState,
                resultInfo?.Code,
                resultInfo?.Message,
                hasMediaStream);

            if (CallDiagnostics.IsEstablishedTransition(oldState, newState))
            {
                if (this.origin == CallOrigin.PolicyRecordingIncoming)
                {
                    this.policyRecordingState = PolicyRecordingCallState.Established;
                    this.logger.LogInformation("Policy recording call established. CallId={CallId}", callId);
                }

                if (!hasMediaStream)
                {
                    this.logger.LogWarning("Call established but BotMediaStream is null. CallId={CallId}", callId);
                }

                this.logger.LogInformation(
                    "Call established. CallId={CallId}; HasMediaStream={HasMediaStream}; UseSpeechService={UseSpeechService}; MediaMode={MediaMode}",
                    callId,
                    hasMediaStream,
                    this.settings.UseSpeechService,
                    CallDiagnostics.GetMediaMode(this.settings.UseSpeechService));

                if (PolicyRecordingLifecycle.ShouldStartRecording(this.origin, oldState, newState))
                {
                    await this.StartMediaAndSpeechPipelineAsync(callId, reportRecordingStatus: true).ConfigureAwait(false);
                }
                else if (this.origin == CallOrigin.CommandJoin)
                {
                    await this.StartMediaAndSpeechPipelineAsync(callId, reportRecordingStatus: false).ConfigureAwait(false);
                }
            }

            if (CallDiagnostics.IsTerminated(oldState, newState))
            {
                if (Interlocked.CompareExchange(ref terminationHandled, 1, 0) != 0)
                {
                    return;
                }

                this.logger.LogInformation(
                    "Call ended / terminated. SessionId={SessionId}; CallId={CallId}; ResultCode={ResultCode}; ResultMessage={ResultMessage}; ReceivedFrames={ReceivedFrames}; SentFrames={SentFrames}",
                    this.sessionId,
                    callId,
                    resultInfo?.Code,
                    resultInfo?.Message,
                    BotMediaStream?.ReceivedAudioFrameCount ?? 0,
                    BotMediaStream?.SentAudioFrameCount ?? 0);

                if (BotMediaStream != null)
                {
                    if (this.origin == CallOrigin.PolicyRecordingIncoming)
                    {
                        this.policyRecordingState = PolicyRecordingCallState.Stopping;
                        this.logger.LogInformation("Stopping persistence. CallId={CallId}; Origin={Origin}", callId, this.origin);
                        this.logger.LogInformation("Persistence stopped. CallId={CallId}; Origin={Origin}", callId, this.origin);
                        await this.StopSpeechAndRecordingAsync(callId).ConfigureAwait(false);
                    }
                    else if (this.origin == CallOrigin.CommandJoin)
                    {
                        await this.HandleCommandJoinEndedAsync(callId, resultInfo?.Message ?? "call terminated").ConfigureAwait(false);
                        await this.StopSpeechAndRecordingAsync(callId).ConfigureAwait(false);
                    }

                    this.logger.LogInformation("BotMediaStream shutdown requested for terminated call. CallId={CallId}", callId);
                    try
                    {
                        await BotMediaStream.ShutdownAsync().ConfigureAwait(false);
                        this.logger.LogInformation("BotMediaStream shutdown completed for terminated call. CallId={CallId}", callId);
                    }
                    catch (Exception ex)
                    {
                        this.GraphLogger.Error(ex);
                        this.logger.LogError(ex, "BotMediaStream shutdown failed for terminated call. CallId={CallId}", callId);
                    }
                }
            }
        }

        private async Task StartMediaAndSpeechPipelineAsync(string callId, bool reportRecordingStatus)
        {
            if (Interlocked.CompareExchange(ref transcriptionStartAttempted, 1, 0) != 0)
            {
                return;
            }

            try
            {
                this.logger.LogInformation(
                    "Speech pipeline starting. CallId={CallId}; SessionId={SessionId}; Origin={Origin}; ReportRecordingStatus={ReportRecordingStatus}",
                    callId,
                    this.sessionId,
                    this.origin,
                    reportRecordingStatus);

                if (reportRecordingStatus)
                {
                    this.logger.LogInformation("Updating recording status to Recording. CallId={CallId}; Origin={Origin}", callId, this.origin);
                    await this.recordingStatusUpdater.UpdateRecordingStatusAsync(this.Call, RecordingStatus.Recording).ConfigureAwait(false);
                    Interlocked.Exchange(ref recordingStarted, 1);
                    this.policyRecordingState = PolicyRecordingCallState.RecordingStatusConfirmed;
                    this.logger.LogInformation("Recording status update succeeded. CallId={CallId}; RecordingStatus={RecordingStatus}; Origin={Origin}", callId, RecordingStatus.Recording, this.origin);
                    this.logger.LogInformation("Persistence allowed. CallId={CallId}; CanPersistMediaOrDerivedData={CanPersistMediaOrDerivedData}", callId, this.CanPersistMediaOrDerivedData);
                }

                if (this.BotMediaStream == null)
                {
                    this.logger.LogWarning("Speech pipeline could not start because BotMediaStream is null. CallId={CallId}; SessionId={SessionId}; Origin={Origin}", callId, this.sessionId, this.origin);
                    return;
                }

                var started = await this.BotMediaStream.StartSpeechTranscriptionAsync().ConfigureAwait(false);
                var snapshot = this.BotMediaStream.SpeechPipelineSnapshot;
                this.logger.LogInformation(
                    "Speech pipeline started. CallId={CallId}; SessionId={SessionId}; Origin={Origin}; SpeechServiceAvailable={SpeechServiceAvailable}; SpeechPipelineReady={SpeechPipelineReady}; SpeechStarted={SpeechStarted}; RecognizerCreated={RecognizerCreated}; PushStreamOpen={PushStreamOpen}; AcceptingFrames={AcceptingFrames}",
                    callId,
                    this.sessionId,
                    this.origin,
                    snapshot.ServiceAvailable,
                    snapshot.Ready,
                    snapshot.Started,
                    snapshot.RecognizerCreated,
                    snapshot.PushStreamOpen,
                    snapshot.AcceptingFrames);

                if (this.origin == CallOrigin.CommandJoin)
                {
                    if (started)
                    {
                        await this.statusReporter.ReportAsync(
                            this.sessionId,
                            BotMeetingStatus.Recording,
                            "recording started",
                            callId).ConfigureAwait(false);
                    }
                    else
                    {
                        this.logger.LogWarning(
                            "Speech pipeline is not ready yet; keeping meeting session joined. CallId={CallId}; SessionId={SessionId}; Origin={Origin}; SpeechPipelineReady={SpeechPipelineReady}; SpeechStarted={SpeechStarted}; RecognizerCreated={RecognizerCreated}; PushStreamOpen={PushStreamOpen}; AcceptingFrames={AcceptingFrames}",
                            callId,
                            this.sessionId,
                            this.origin,
                            snapshot.Ready,
                            snapshot.Started,
                            snapshot.RecognizerCreated,
                            snapshot.PushStreamOpen,
                            snapshot.AcceptingFrames);

                        await this.statusReporter.ReportAsync(
                            this.sessionId,
                            BotMeetingStatus.Joined,
                            "speech pipeline is still starting",
                            callId,
                            failedReason: "speech_pipeline_not_ready",
                            errorCode: "SpeechPipelineNotReady",
                            source: "speech_pipeline").ConfigureAwait(false);
                    }
                }
            }
            catch (Exception ex)
            {
                this.GraphLogger.Error(ex);
                this.logger.LogError(
                    ex,
                    "Speech pipeline start failed. CallId={CallId}; SessionId={SessionId}; Origin={Origin}",
                    callId,
                    this.sessionId,
                    this.origin);
                if (this.origin == CallOrigin.CommandJoin)
                {
                    await this.statusReporter.ReportAsync(
                        this.sessionId,
                        BotMeetingStatus.Joined,
                        "speech pipeline failed to start; meeting remains joined",
                        callId,
                        failedReason: "speech_pipeline_start_failed",
                        errorCode: ex.GetType().Name,
                        source: "speech_pipeline").ConfigureAwait(false);
                }
            }
        }

        private async Task StopSpeechAndRecordingAsync(string callId)
        {
            if (this.BotMediaStream != null)
            {
                await this.BotMediaStream.StopSpeechTranscriptionAsync().ConfigureAwait(false);
            }

            if (Interlocked.CompareExchange(ref recordingStarted, 0, 1) != 1)
            {
                return;
            }

            try
            {
                this.logger.LogInformation("Updating recording status to NotRecording. CallId={CallId}", callId);
                await this.recordingStatusUpdater.UpdateRecordingStatusAsync(this.Call, RecordingStatus.NotRecording).ConfigureAwait(false);
                this.logger.LogInformation("Recording status update succeeded. CallId={CallId}; RecordingStatus={RecordingStatus}", callId, RecordingStatus.NotRecording);
            }
            catch (Exception ex)
            {
                this.GraphLogger.Error(ex);
                this.logger.LogError(ex, "Failed to update recording status to NotRecording. CallId={CallId}", callId);
            }
        }

        public CallOrigin Origin => this.origin;

        public bool CanPersistMediaOrDerivedData =>
            PolicyRecordingLifecycle.CanPersistMediaOrDerivedData(
                this.origin,
                this.policyRecordingState,
                Volatile.Read(ref terminationHandled) != 0);

        /// <summary>
        /// Creates the participant update json.
        /// </summary>
        /// <param name="participantId">The participant identifier.</param>
        /// <param name="participantDisplayName">Display name of the participant.</param>
        /// <returns>System.String.</returns>
        private string createParticipantUpdateJson(string participantId, string participantDisplayName = "")
        {
            if (participantDisplayName.Length == 0)
                return "{" + String.Format($"\"Id\": \"{participantId}\"") + "}";
            else
                return "{" + String.Format($"\"Id\": \"{participantId}\", \"DisplayName\": \"{participantDisplayName}\"") + "}";
        }

        /// <summary>
        /// Updates the participant.
        /// </summary>
        /// <param name="participants">The participants.</param>
        /// <param name="participant">The participant.</param>
        /// <param name="added">if set to <c>true</c> [added].</param>
        /// <param name="participantDisplayName">Display name of the participant.</param>
        /// <returns>System.String.</returns>
        private string updateParticipant(List<IParticipant> participants, IParticipant participant, bool added, string participantDisplayName = "")
        {
            if (added)
                participants.Add(participant);
            else
                participants.Remove(participant);
            return createParticipantUpdateJson(participant.Id, participantDisplayName);
        }

        /// <summary>
        /// Updates the participants.
        /// </summary>
        /// <param name="eventArgs">The event arguments.</param>
        /// <param name="added">if set to <c>true</c> [added].</param>
        private void updateParticipants(ICollection<IParticipant> eventArgs, bool added = true)
        {
            foreach (var participant in eventArgs)
            {
                var json = string.Empty;

                // todo remove the cast with the new graph implementation,
                // for now we want the bot to only subscribe to "real" participants
                var participantDetails = participant.Resource.Info.Identity.User;

                if (participantDetails != null)
                {
                    json = updateParticipant(this.BotMediaStream.participants, participant, added, participantDetails.DisplayName);
                    updateParticipantSpeakerMap(participant, added, participantDetails.DisplayName);
                }
                else if (participant.Resource.Info.Identity.AdditionalData?.Count > 0)
                {
                    if (CheckParticipantIsUsable(participant))
                    {
                        json = updateParticipant(this.BotMediaStream.participants, participant, added);
                        updateParticipantSpeakerMap(participant, added, GetParticipantDisplayName(participant));
                    }
                }
            }
        }

        private void updateParticipantSpeakerMap(IParticipant participant, bool added, string? displayName)
        {
            var mediaStreams = participant.Resource?.MediaStreams;
            if (mediaStreams == null || this.BotMediaStream == null)
            {
                return;
            }

            foreach (var mediaStream in mediaStreams)
            {
                var sourceId = mediaStream.SourceId?.ToString();
                if (string.IsNullOrWhiteSpace(sourceId))
                {
                    continue;
                }

                if (added)
                {
                    this.BotMediaStream.RegisterParticipantSpeaker(sourceId, displayName, participant.Id);
                }
                else
                {
                    this.BotMediaStream.UnregisterParticipantSpeaker(sourceId);
                }
            }
        }

        private static string? GetParticipantDisplayName(IParticipant participant)
        {
            var identity = participant.Resource?.Info?.Identity;
            var userName = identity?.User?.DisplayName;
            if (!string.IsNullOrWhiteSpace(userName))
            {
                return userName;
            }

            if (identity?.AdditionalData == null)
            {
                return null;
            }

            foreach (var value in identity.AdditionalData.Values)
            {
                if (value is Identity additionalIdentity && !string.IsNullOrWhiteSpace(additionalIdentity.DisplayName))
                {
                    return additionalIdentity.DisplayName;
                }
            }

            return null;
        }

        /// <summary>
        /// Event fired when the participants collection has been updated.
        /// </summary>
        /// <param name="sender">Participants collection.</param>
        /// <param name="args">Event args containing added and removed participants.</param>
        public void ParticipantsOnUpdated(IParticipantCollection sender, CollectionEventArgs<IParticipant> args)
        {
            updateParticipants(args.AddedResources);
            updateParticipants(args.RemovedResources, false);
            this.EvaluateBotOnlyParticipants();
        }

        private void EvaluateBotOnlyParticipants()
        {
            if (this.origin != CallOrigin.CommandJoin || this.Call.Resource?.State != CallState.Established)
            {
                return;
            }

            var participantCount = this.BotMediaStream?.participants?.Count ?? 0;
            if (participantCount > 0)
            {
                this.CancelBotOnlyLeave();
                return;
            }

            if (this.botOnlyLeaveCts != null)
            {
                return;
            }

            var callId = this.Call?.Id ?? string.Empty;
            this.logger.LogWarning(
                "Bot-only detected. SessionId={SessionId}; CallId={CallId}; ParticipantCount={ParticipantCount}; GraceSeconds={GraceSeconds}",
                this.sessionId,
                callId,
                participantCount,
                30);

            var cts = new CancellationTokenSource();
            this.botOnlyLeaveCts = cts;
            _ = this.LeaveAfterBotOnlyGraceAsync(callId, cts.Token)
                .ForgetAndLogExceptionAsync(this.GraphLogger, "Bot-only leave failed");
        }

        private async Task LeaveAfterBotOnlyGraceAsync(string callId, CancellationToken cancellationToken)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            if (this.origin != CallOrigin.CommandJoin || this.Call.Resource?.State != CallState.Established)
            {
                return;
            }

            var participantCount = this.BotMediaStream?.participants?.Count ?? 0;
            if (participantCount > 0)
            {
                return;
            }

            this.logger.LogWarning(
                "Leaving call. Reason=BotOnly; SessionId={SessionId}; CallId={CallId}; ParticipantCount={ParticipantCount}",
                this.sessionId,
                callId,
                participantCount);

            try
            {
                await this.Call.DeleteAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                this.logger.LogError(ex, "Failed to leave bot-only call. SessionId={SessionId}; CallId={CallId}", this.sessionId, callId);
            }
        }

        private void CancelBotOnlyLeave()
        {
            var cts = this.botOnlyLeaveCts;
            if (cts == null)
            {
                return;
            }

            this.botOnlyLeaveCts = null;
            cts.Cancel();
            cts.Dispose();
        }

        private void StartDeciScopeHeartbeat()
        {
            if (this.origin != CallOrigin.CommandJoin || string.IsNullOrWhiteSpace(this.sessionId))
            {
                return;
            }

            var interval = this.statusReporter.HeartbeatInterval;
            if (interval == null || interval.Value <= TimeSpan.Zero)
            {
                return;
            }

            if (this.deciScopeHeartbeatCts != null)
            {
                return;
            }

            var cts = new CancellationTokenSource();
            this.deciScopeHeartbeatCts = cts;
            var token = cts.Token;
            var heartbeatInterval = interval.Value;

            async Task RunLoop()
            {
                try
                {
                    while (!token.IsCancellationRequested)
                    {
                        await Task.Delay(heartbeatInterval, token).ConfigureAwait(false);

                        if (Volatile.Read(ref this.terminationHandled) != 0)
                        {
                            return;
                        }

                        await this.statusReporter.ReportHeartbeatAsync(this.sessionId, this.Call?.Id, token).ConfigureAwait(false);
                    }
                }
                catch (OperationCanceledException)
                {
                }
            }

            _ = RunLoop().ForgetAndLogExceptionAsync(this.GraphLogger, "DeciScope heartbeat loop failed");
        }

        private void StopDeciScopeHeartbeat()
        {
            var cts = this.deciScopeHeartbeatCts;
            if (cts == null)
            {
                return;
            }

            this.deciScopeHeartbeatCts = null;
            cts.Cancel();
            cts.Dispose();
        }

        private async Task HandleCommandJoinEndedAsync(string callId, string reason)
        {
            this.CancelBotOnlyLeave();
            this.StopDeciScopeHeartbeat();
            var source = string.Equals(reason, "shutdown", StringComparison.OrdinalIgnoreCase)
                ? "bot_shutdown"
                : "bot_call_state";
            this.logger.LogInformation(
                "Call ended detected. SessionId={SessionId}; CallId={CallId}; Reason={Reason}; Source={Source}",
                this.sessionId,
                callId,
                reason,
                source);

            this.logger.LogInformation(
                "Report ended started. SessionId={SessionId}; CallId={CallId}; Reason={Reason}; Source={Source}",
                this.sessionId,
                callId,
                reason,
                source);
            await this.statusReporter.ReportAsync(
                this.sessionId,
                BotMeetingStatus.Ended,
                reason,
                callId,
                CancellationToken.None,
                source: source,
                endReason: reason,
                endedAt: DateTimeOffset.UtcNow).ConfigureAwait(false);
            this.logger.LogInformation(
                "Report ended completed. SessionId={SessionId}; CallId={CallId}; Reason={Reason}; Source={Source}",
                this.sessionId,
                callId,
                reason,
                source);

            if (this.callEndedCallback != null)
            {
                await this.callEndedCallback(this.sessionId, callId, reason).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Checks the participant is usable.
        /// </summary>
        /// <param name="p">The p.</param>
        /// <returns><c>true</c> if XXXX, <c>false</c> otherwise.</returns>
        private bool CheckParticipantIsUsable(IParticipant p)
        {
            foreach (var i in p.Resource.Info.Identity.AdditionalData)
                if (i.Key != "applicationInstance" && i.Value is Identity)
                    return true;

            return false;
        }
    }
}

