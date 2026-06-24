using EchoBot.Util;
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
        private readonly CallOrigin origin;
        private int recordingStarted;
        private int transcriptionStartAttempted;
        private int terminationHandled;
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
            CallOrigin origin = CallOrigin.OutboundJoin,
            ILocalMediaSession? localMediaSession = null
        )
            : base(TimeSpan.FromMinutes(10), statefulCall.GraphLogger)
        {
            this.Call = statefulCall;
            this.settings = settings;
            this.logger = logger;
            this.recordingStatusUpdater = recordingStatusUpdater;
            this.origin = origin;
            this.Call.OnUpdated += this.CallOnUpdated;
            this.Call.Participants.OnUpdated += this.ParticipantsOnUpdated;

            this.BotMediaStream = new BotMediaStream(localMediaSession ?? this.Call.GetLocalMediaSession(), this.Call.Id, this.GraphLogger, logger, settings);

            this.logger.LogInformation(
                "CallHandler initialized. CallId={CallId}; Origin={Origin}; HasMediaStream={HasMediaStream}; MediaMode={MediaMode}",
                this.Call.Id,
                this.origin,
                this.BotMediaStream != null,
                CallDiagnostics.GetMediaMode(this.settings.UseSpeechService));

            if (this.origin == CallOrigin.PolicyRecordingIncoming)
            {
                this.logger.LogInformation("Policy recording CallHandler initialized. CallId={CallId}", this.Call.Id);
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

            _ = this.ShutdownAsync().ForgetAndLogExceptionAsync(this.GraphLogger);
        }

        public async Task ShutdownAsync()
        {
            var callId = this.Call?.Id ?? string.Empty;
            if (Interlocked.CompareExchange(ref terminationHandled, 1, 0) == 0 && this.origin == CallOrigin.PolicyRecordingIncoming)
            {
                this.policyRecordingState = PolicyRecordingCallState.Stopping;
                this.logger.LogInformation("Stopping persistence. CallId={CallId}; Origin={Origin}", callId, this.origin);
                this.logger.LogInformation("Persistence stopped. CallId={CallId}; Origin={Origin}", callId, this.origin);
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
                    await this.StartRecordingAndSpeechAsync(callId).ConfigureAwait(false);
                }
            }

            if (CallDiagnostics.IsTerminatedFromEstablished(oldState, newState))
            {
                if (Interlocked.CompareExchange(ref terminationHandled, 1, 0) != 0)
                {
                    return;
                }

                this.logger.LogInformation(
                    "Call terminated. CallId={CallId}; ResultCode={ResultCode}; ResultMessage={ResultMessage}; ReceivedFrames={ReceivedFrames}; SentFrames={SentFrames}",
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

        private async Task StartRecordingAndSpeechAsync(string callId)
        {
            if (PolicyRecordingLifecycle.ShouldSkipRecording(this.origin))
            {
                this.logger.LogDebug("Recording status update skipped. CallId={CallId}; Origin={Origin}", callId, this.origin);
                return;
            }

            if (Interlocked.CompareExchange(ref transcriptionStartAttempted, 1, 0) != 0)
            {
                return;
            }

            try
            {
                this.logger.LogInformation("Updating recording status to Recording. CallId={CallId}", callId);
                await this.recordingStatusUpdater.UpdateRecordingStatusAsync(this.Call, RecordingStatus.Recording).ConfigureAwait(false);
                Interlocked.Exchange(ref recordingStarted, 1);
                this.policyRecordingState = PolicyRecordingCallState.RecordingStatusConfirmed;
                this.logger.LogInformation("Recording status update succeeded. CallId={CallId}; RecordingStatus={RecordingStatus}", callId, RecordingStatus.Recording);
                this.logger.LogInformation("Persistence allowed. CallId={CallId}; CanPersistMediaOrDerivedData={CanPersistMediaOrDerivedData}", callId, this.CanPersistMediaOrDerivedData);

                if (this.BotMediaStream == null)
                {
                    this.logger.LogWarning("Recording status was updated but BotMediaStream is null. CallId={CallId}", callId);
                    return;
                }

                await this.BotMediaStream.StartSpeechTranscriptionAsync().ConfigureAwait(false);
                this.logger.LogInformation("Azure Speech started. CallId={CallId}", callId);
            }
            catch (Exception ex)
            {
                this.GraphLogger.Error(ex);
                this.logger.LogError(
                    ex,
                    "Recording status update or Speech transcription start failed. Speech audio will not be sent. CallId={CallId}",
                    callId);
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
                }
                else if (participant.Resource.Info.Identity.AdditionalData?.Count > 0)
                {
                    if (CheckParticipantIsUsable(participant))
                    {
                        json = updateParticipant(this.BotMediaStream.participants, participant, added);
                    }
                }
            }
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

