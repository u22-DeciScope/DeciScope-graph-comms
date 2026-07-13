namespace EchoBot.Services
{
    /// <summary>
    /// The speech-side surface the meeting-end drain needs. Implemented by
    /// BotMediaStream; kept as an interface so the drain sequence is unit
    /// testable without Graph/Speech SDK objects.
    /// </summary>
    public interface ISpeechTranscriptionShutdown
    {
        Task StopSpeechTranscriptionAsync(CancellationToken cancellationToken = default);

        Task<bool> WaitForRecognitionCallbacksAsync(CancellationToken cancellationToken = default);

        int PendingRecognitionCallbackCount { get; }
    }

    /// <summary>
    /// Result of the meeting-end drain sequence.
    /// </summary>
    public sealed record MeetingEndDrainOutcome(
        bool SpeechStopped,
        bool CallbacksDrained,
        TranscriptDrainResult TranscriptDrain,
        string? TimeoutReason)
    {
        /// <summary>
        /// True only when every stage finished: the recognizer stopped, every
        /// started Recognized callback completed, and the session's forwarding
        /// queue fully drained. Never reported true on a timeout.
        /// </summary>
        public bool TranscriptQueueDrained => SpeechStopped && CallbacksDrained && TranscriptDrain.Drained;

        /// <summary>Highest final sequence whose HTTP forward succeeded, or null.</summary>
        public long? LastFinalSequenceNo => TranscriptDrain.LastFinalSequenceNo;
    }

    /// <summary>
    /// Runs the meeting-end drain sequence exactly once per call:
    /// speech recognizer stop → Recognized callback drain → session transcript
    /// queue drain. Concurrent/duplicate end triggers (Graph terminated event,
    /// bot shutdown, repeated end commands) all await the same task, so the
    /// sequence and the resulting drain numbers can never run twice.
    /// </summary>
    public sealed class MeetingEndDrainCoordinator
    {
        private readonly ITranscriptForwarder transcriptForwarder;
        private readonly TranscriptForwardingOptions options;
        private readonly ILogger logger;
        private readonly object gate = new object();
        private Task<MeetingEndDrainOutcome>? drainTask;

        public MeetingEndDrainCoordinator(
            ITranscriptForwarder transcriptForwarder,
            TranscriptForwardingOptions options,
            ILogger logger)
        {
            this.transcriptForwarder = transcriptForwarder;
            this.options = options;
            this.logger = logger;
        }

        public Task<MeetingEndDrainOutcome> DrainAsync(
            string? sessionId,
            string callId,
            string stopReason,
            ISpeechTranscriptionShutdown speech)
        {
            lock (gate)
            {
                drainTask ??= ExecuteAsync(sessionId, callId, stopReason, speech);
                return drainTask;
            }
        }

        private async Task<MeetingEndDrainOutcome> ExecuteAsync(
            string? sessionId,
            string callId,
            string stopReason,
            ISpeechTranscriptionShutdown speech)
        {
            logger.LogInformation(
                "Meeting transcript drain started. SessionId={SessionId}; CallId={CallId}; StopReason={StopReason}; PendingRecognizedCallbacks={PendingRecognizedCallbacks}; SpeechStopTimeout={SpeechStopTimeout}; CallbackDrainTimeout={CallbackDrainTimeout}; TranscriptDrainTimeout={TranscriptDrainTimeout}",
                sessionId,
                callId,
                stopReason,
                speech.PendingRecognitionCallbackCount,
                options.SpeechStopTimeout,
                options.CallbackDrainTimeout,
                options.TranscriptDrainTimeout);

            string? timeoutReason = null;

            // 1) Speech recognizer停止。SDK内部待ちがtokenを尊重しない可能性が
            //    あるため、WhenAnyで打ち切り判定し、停止Task自体は観測を続けて
            //    未観測例外にしない。
            var speechStopStartedAt = DateTimeOffset.UtcNow;
            var speechStopped = true;
            var stopTask = StopSpeechSafelyAsync(speech, callId);
            var stopFinished = await Task.WhenAny(stopTask, Task.Delay(options.SpeechStopTimeout)).ConfigureAwait(false) == stopTask;
            if (!stopFinished)
            {
                speechStopped = false;
                timeoutReason = "speech_stop_timeout";
                logger.LogWarning(
                    "Speech stop timed out during meeting end drain. SessionId={SessionId}; CallId={CallId}; SpeechStopTimeout={SpeechStopTimeout}",
                    sessionId,
                    callId,
                    options.SpeechStopTimeout);
            }
            logger.LogInformation(
                "Speech stop stage finished. SessionId={SessionId}; CallId={CallId}; SpeechStopStarted={SpeechStopStarted}; SpeechStopCompleted={SpeechStopCompleted}; SpeechStopped={SpeechStopped}",
                sessionId,
                callId,
                speechStopStartedAt,
                DateTimeOffset.UtcNow,
                speechStopped);

            // 2) 実行中のRecognized callback完了待ち。
            bool callbacksDrained;
            using (var callbackCts = new CancellationTokenSource(options.CallbackDrainTimeout))
            {
                callbacksDrained = await speech.WaitForRecognitionCallbacksAsync(callbackCts.Token).ConfigureAwait(false);
            }
            if (!callbacksDrained)
            {
                timeoutReason ??= "callback_drain_timeout";
                logger.LogWarning(
                    "Recognized callback drain timed out during meeting end drain. SessionId={SessionId}; CallId={CallId}; PendingRecognizedCallbacks={PendingRecognizedCallbacks}; CallbackDrainTimeout={CallbackDrainTimeout}",
                    sessionId,
                    callId,
                    speech.PendingRecognitionCallbackCount,
                    options.CallbackDrainTimeout);
            }
            logger.LogInformation(
                "Recognized callback drain stage finished. SessionId={SessionId}; CallId={CallId}; CallbackDrainCompleted={CallbackDrainCompleted}; PendingRecognizedCallbacks={PendingRecognizedCallbacks}",
                sessionId,
                callId,
                callbacksDrained,
                speech.PendingRecognitionCallbackCount);

            // 3) セッションのforwarding queue drain (HTTP転送完了まで)。
            TranscriptDrainResult drain;
            using (var drainCts = new CancellationTokenSource(options.TranscriptDrainTimeout))
            {
                drain = await transcriptForwarder.DrainSessionAsync(sessionId, drainCts.Token).ConfigureAwait(false);
            }
            if (!drain.Drained)
            {
                timeoutReason ??= "transcript_drain_timeout";
            }

            var outcome = new MeetingEndDrainOutcome(speechStopped, callbacksDrained, drain, timeoutReason);
            logger.LogInformation(
                "Meeting transcript drain completed. SessionId={SessionId}; CallId={CallId}; SpeechStopped={SpeechStopped}; CallbackDrainCompleted={CallbackDrainCompleted}; TranscriptDrainCompleted={TranscriptDrainCompleted}; QueuedTranscriptCount={QueuedTranscriptCount}; FailedTranscriptCount={FailedTranscriptCount}; LastForwardedFinalSequenceNo={LastForwardedFinalSequenceNo}; TranscriptQueueDrained={TranscriptQueueDrained}; DrainTimeout={DrainTimeout}",
                sessionId,
                callId,
                speechStopped,
                callbacksDrained,
                drain.Drained,
                drain.PendingCount,
                drain.FailedCount,
                outcome.LastFinalSequenceNo,
                outcome.TranscriptQueueDrained,
                timeoutReason);
            return outcome;
        }

        private async Task StopSpeechSafelyAsync(ISpeechTranscriptionShutdown speech, string callId)
        {
            try
            {
                await speech.StopSpeechTranscriptionAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Speech stop failed during meeting end drain. CallId={CallId}", callId);
            }
        }
    }
}
