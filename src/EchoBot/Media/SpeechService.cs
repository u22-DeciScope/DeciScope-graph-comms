using Microsoft.CognitiveServices.Speech;
using Microsoft.CognitiveServices.Speech.Audio;
using EchoBot.Models;
using EchoBot.Services;

namespace EchoBot.Media
{
    public sealed class SpeechService : IAsyncDisposable
    {
        private readonly string callId;
        private readonly ILogger logger;
        private readonly SpeechTranscriptionSettings settings;
        private readonly ITranscriptSequenceProvider transcriptSequenceProvider;
        private readonly ITranscriptForwarder transcriptForwarder;
        private readonly IBotMeetingStatusReporter statusReporter;
        private readonly ISpeechStatusSink? speechStatusSink;
        private readonly string statusInstanceId;
        private readonly string? speakerId;
        private string? speakerName;
        private string? sessionId;
        private readonly BoundedAudioFrameQueue audioQueue;
        private readonly SemaphoreSlim lifecycleLock = new SemaphoreSlim(1, 1);
        private readonly CancellationTokenSource stopCts = new CancellationTokenSource();

        private PushAudioInputStream? audioInputStream;
        private AudioConfig? audioConfig;
        private SpeechRecognizer? recognizer;
        private Task? queuePumpTask;
        private int started;
        private int stopping;
        private int acceptingFrames;
        private int reconnectScheduled;
        private int reconnectPending;
        private int reconnectAttempt;
        private long lastNonEmptyTranscriptAtUtcTicks;
        private long lastFinalTranscriptAtUtcTicks;
        private readonly object pipelineTelemetryGate = new object();
        private bool telemetryStarted;
        private bool telemetryAcceptingFrames;
        private bool telemetryRecognizerCreated;
        private bool telemetryPushStreamOpen;
        private long pipelineGeneration;
        private string? recognizerInstanceIdHash;
        private long lastRecognizerStartedAtUtcTicks;
        private long lastSpeechPartialAtUtcTicks;
        private long lastSpeechFinalAtUtcTicks;

        // Recognized callback completion tracking. The Speech SDK fires
        // Recognized synchronously, but the transcript save/forward work it
        // starts (SaveRecognizedSpeechAsync) used to be fire-and-forget, so a
        // meeting could end while the last final transcript was still being
        // enqueued. The counter + waiters below make that work awaitable at
        // shutdown without changing when it runs.
        private readonly object callbackGate = new object();
        private readonly List<TaskCompletionSource<bool>> callbackWaiters = new List<TaskCompletionSource<bool>>();
        private int pendingRecognitionCallbacks;

        private static readonly TimeSpan MaxReconnectDelay = TimeSpan.FromSeconds(30);

        /// <summary>
        /// Instance id used to key speech status in the session-level aggregator when this SpeechService
        /// is the mixed-audio fallback recognizer (i.e. no speakerId was supplied).
        /// </summary>
        internal const string MixedFallbackInstanceId = "mixed-fallback";

        public SpeechService(
            string callId,
            AppSettings appSettings,
            ILogger logger,
            ITranscriptSequenceProvider transcriptSequenceProvider,
            ITranscriptForwarder transcriptForwarder,
            IBotMeetingStatusReporter statusReporter,
            string? sessionId = null,
            string? speakerId = null,
            string? speakerName = null,
            ISpeechStatusSink? speechStatusSink = null)
        {
            this.callId = callId;
            this.logger = logger;
            this.transcriptSequenceProvider = transcriptSequenceProvider;
            this.transcriptForwarder = transcriptForwarder;
            this.statusReporter = statusReporter;
            this.speechStatusSink = speechStatusSink;
            this.sessionId = sessionId;
            this.speakerId = speakerId;
            this.speakerName = NormalizeSpeakerName(speakerName);
            this.statusInstanceId = !string.IsNullOrWhiteSpace(speakerId) ? speakerId! : MixedFallbackInstanceId;
            settings = SpeechTranscriptionSettings.FromAppSettings(appSettings);
            audioQueue = new BoundedAudioFrameQueue(settings.AudioQueueCapacity);
        }

        public long DroppedFrames => audioQueue.DroppedFrames;

        public bool IsStarted => Volatile.Read(ref started) == 1;

        /// <summary>
        /// UTC time of the most recent final recognition result with non-empty text, or null if none has
        /// been recognized yet.
        /// </summary>
        public DateTimeOffset? LastNonEmptyTranscriptAtUtc => TicksToUtc(Interlocked.Read(ref lastNonEmptyTranscriptAtUtcTicks));

        /// <summary>
        /// UTC time of the most recent final recognition result received by this instance (regardless of
        /// whether its text was empty), or null if none has been received yet.
        /// </summary>
        public DateTimeOffset? LastFinalTranscriptAtUtc => TicksToUtc(Interlocked.Read(ref lastFinalTranscriptAtUtcTicks));

        public void SetSessionId(string? value)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                sessionId = value;
            }
        }

        public void SetSpeakerName(string? value)
        {
            var normalized = NormalizeSpeakerName(value);
            if (!string.IsNullOrWhiteSpace(normalized))
            {
                speakerName = normalized;
            }
        }

        public SpeechPipelineSnapshot Snapshot
        {
            get
            {
                lock (pipelineTelemetryGate)
                {
                    return new SpeechPipelineSnapshot(
                        serviceAvailable: true,
                        started: telemetryStarted,
                        acceptingFrames: telemetryAcceptingFrames,
                        recognizerCreated: telemetryRecognizerCreated,
                        pushStreamOpen: telemetryPushStreamOpen,
                        droppedFrames: audioQueue.DroppedFrames,
                        recognizerInstanceIdHash: recognizerInstanceIdHash,
                        pipelineGeneration: pipelineGeneration,
                        lastRecognizerStartedAtUtc: TicksToUtc(lastRecognizerStartedAtUtcTicks),
                        lastSpeechPartialAtUtc: TicksToUtc(lastSpeechPartialAtUtcTicks),
                        lastSpeechFinalAtUtc: TicksToUtc(lastSpeechFinalAtUtcTicks));
                }
            }
        }

        public async Task StartAsync(CancellationToken cancellationToken = default)
        {
            if (Interlocked.CompareExchange(ref started, 1, 0) != 0)
            {
                return;
            }

            await lifecycleLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                logger.LogInformation(
                    "Starting Azure Speech transcription. CallId={CallId}; SpeakerId={SpeakerId}; SpeakerName={SpeakerName}; Language={Language}; QueueCapacity={QueueCapacity}; SegmentationSilenceTimeoutMs={SegmentationSilenceTimeoutMs}; LogTranscripts={LogTranscripts}",
                    callId,
                    speakerId,
                    speakerName,
                    settings.RecognitionLanguage,
                    settings.AudioQueueCapacity,
                    settings.SegmentationSilenceTimeoutMs,
                    settings.LogTranscripts);

                CreateRecognizer();
                queuePumpTask = Task.Run(() => PumpAudioAsync(stopCts.Token));
                await recognizer!.StartContinuousRecognitionAsync().ConfigureAwait(false);
                MarkRecognizerStarted();

                logger.LogInformation("Speech recognition session started. CallId={CallId}; SpeakerId={SpeakerId}; SpeakerName={SpeakerName}", callId, speakerId, speakerName);
                _ = ReportSpeechStatusSafelyAsync(
                    BotMeetingStatus.Recording,
                    "speech recognizer started",
                    "azure_speech_started",
                    null);
            }
            catch
            {
                MarkAcceptingFrames(false);
                Volatile.Write(ref started, 0);
                await DisposeRecognizerResourcesAsync().ConfigureAwait(false);
                throw;
            }
            finally
            {
                lifecycleLock.Release();
            }
        }

        public bool TryEnqueueAudio(byte[] pcm, out string? dropReason)
        {
            dropReason = null;
            if (Volatile.Read(ref acceptingFrames) != 1)
            {
                dropReason = Volatile.Read(ref started) == 1
                    ? "SpeechPipelineNotReady"
                    : "SpeechPipelineNotStarted";
                return false;
            }

            var accepted = audioQueue.TryEnqueue(pcm);
            if (!accepted && ShouldLogDroppedFrame(audioQueue.DroppedFrames))
            {
                dropReason = "SpeechQueueFull";
                logger.LogWarning(
                    "Speech audio frame dropped because the queue is full. CallId={CallId}; DroppedFrames={DroppedFrames}",
                    callId,
                    audioQueue.DroppedFrames);
            }

            if (!accepted && dropReason == null)
            {
                dropReason = "SpeechQueueUnavailable";
            }

            return accepted;
        }

        public async Task StopAsync(CancellationToken cancellationToken = default)
        {
            if (Interlocked.CompareExchange(ref stopping, 1, 0) != 0)
            {
                return;
            }

            MarkAcceptingFrames(false);
            stopCts.Cancel();
            audioQueue.Complete();

            await lifecycleLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                logger.LogInformation("Stopping Azure Speech transcription. CallId={CallId}; SpeakerId={SpeakerId}; SpeakerName={SpeakerName}", callId, speakerId, speakerName);

                if (queuePumpTask != null)
                {
                    await queuePumpTask.ConfigureAwait(false);
                }

                await DisposeRecognizerResourcesAsync().ConfigureAwait(false);

                logger.LogInformation(
                    "Azure Speech transcription stopped. CallId={CallId}; DroppedFrames={DroppedFrames}",
                    callId,
                    audioQueue.DroppedFrames);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to stop Azure Speech transcription. CallId={CallId}", callId);
            }
            finally
            {
                Volatile.Write(ref started, 0);
                lifecycleLock.Release();
            }

            if (speechStatusSink != null)
            {
                try
                {
                    // Always remove this instance from the session-level aggregate when it stops, whether
                    // it stopped normally (e.g. mixed fallback yielding to unmixed audio) or due to an
                    // error. The aggregator recomputes the session status from whatever instances remain,
                    // so this is not itself treated as an error condition.
                    await speechStatusSink.RemoveAsync(statusInstanceId).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(
                        ex,
                        "Speech status sink removal failed. CallId={CallId}; SpeakerId={SpeakerId}; SpeakerName={SpeakerName}",
                        callId,
                        speakerId,
                        speakerName);
                }
            }
        }

        public async ValueTask DisposeAsync()
        {
            await StopAsync().ConfigureAwait(false);
            stopCts.Dispose();
            lifecycleLock.Dispose();
        }

        /// <summary>
        /// Number of Recognized callbacks whose transcript save/forward work
        /// has started but not finished yet. Diagnostic only.
        /// </summary>
        public int PendingRecognitionCallbacks
        {
            get
            {
                lock (callbackGate)
                {
                    return pendingRecognitionCallbacks;
                }
            }
        }

        /// <summary>
        /// Waits until every started Recognized callback finished its save/
        /// forward work. Returns false when the token was cancelled while
        /// callbacks were still pending. New callbacks cannot start once the
        /// recognizer has been stopped (StopAsync), so awaiting this after
        /// StopAsync gives a stable "no more final transcripts" boundary.
        /// </summary>
        public async Task<bool> WaitForRecognitionCallbacksAsync(CancellationToken cancellationToken = default)
        {
            while (true)
            {
                TaskCompletionSource<bool> waiter;
                lock (callbackGate)
                {
                    if (pendingRecognitionCallbacks <= 0)
                    {
                        return true;
                    }

                    waiter = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                    callbackWaiters.Add(waiter);
                }

                using (cancellationToken.Register(() => waiter.TrySetResult(false)))
                {
                    await waiter.Task.ConfigureAwait(false);
                }

                if (cancellationToken.IsCancellationRequested)
                {
                    lock (callbackGate)
                    {
                        callbackWaiters.Remove(waiter);
                        return pendingRecognitionCallbacks <= 0;
                    }
                }
            }
        }

        // internal for tests: lets EchoBot.Tests exercise the tracking without
        // firing real Speech SDK events.
        internal void TrackRecognitionCallback(Func<Task> callback)
        {
            lock (callbackGate)
            {
                pendingRecognitionCallbacks++;
            }

            _ = RunTrackedRecognitionCallbackAsync(callback);
        }

        private async Task RunTrackedRecognitionCallbackAsync(Func<Task> callback)
        {
            try
            {
                await callback().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // SaveRecognizedSpeechAsync handles its own errors; this guard
                // keeps a truly unexpected failure observed instead of becoming
                // an unobserved task exception.
                logger.LogError(
                    ex,
                    "Speech recognition callback failed. CallId={CallId}; SpeakerId={SpeakerId}; SpeakerName={SpeakerName}",
                    callId,
                    speakerId,
                    speakerName);
            }
            finally
            {
                List<TaskCompletionSource<bool>>? toRelease = null;
                lock (callbackGate)
                {
                    pendingRecognitionCallbacks--;
                    if (pendingRecognitionCallbacks <= 0 && callbackWaiters.Count > 0)
                    {
                        toRelease = new List<TaskCompletionSource<bool>>(callbackWaiters);
                        callbackWaiters.Clear();
                    }
                }

                if (toRelease != null)
                {
                    foreach (var waiter in toRelease)
                    {
                        waiter.TrySetResult(true);
                    }
                }
            }
        }

        internal static bool ShouldLogDroppedFrame(long droppedFrames)
        {
            return droppedFrames == 1 || droppedFrames % 250 == 0;
        }

        private async Task PumpAudioAsync(CancellationToken cancellationToken)
        {
            try
            {
                await foreach (var frame in audioQueue.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
                {
                    var stream = audioInputStream;
                    if (stream == null)
                    {
                        continue;
                    }

                    try
                    {
                        stream.Write(frame);
                    }
                    catch (Exception ex) when (ex is ObjectDisposedException || ex is InvalidOperationException)
                    {
                        logger.LogWarning(
                            ex,
                            "Speech audio frame write skipped because the recognizer stream is unavailable. CallId={CallId}; SpeakerId={SpeakerId}; SpeakerName={SpeakerName}",
                            callId,
                            speakerId,
                            speakerName);
                    }
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Speech audio queue processing failed. CallId={CallId}", callId);
            }
        }

        private void AttachRecognizerEvents(
            SpeechRecognizer speechRecognizer,
            long recognizerGeneration,
            string instanceIdHash)
        {
            speechRecognizer.SessionStarted += (_, e) =>
            {
                logger.LogInformation("Speech session started. CallId={CallId}; SpeakerId={SpeakerId}; SpeakerName={SpeakerName}; SessionId={SessionId}", callId, speakerId, speakerName, e.SessionId);
            };

            speechRecognizer.Recognizing += (_, e) =>
            {
                ResetReconnectAttempt();
                RecordRecognizerCallback(isFinal: false, recognizerGeneration, instanceIdHash);
                LogSpeechResult(LogLevel.Debug, "Speech partial emitted.", e.Result, isFinal: false);
                _ = ForwardRecognizingSpeechAsync(e.Result);
            };

            speechRecognizer.Recognized += (_, e) =>
            {
                ResetReconnectAttempt();
                if (e.Result.Reason == ResultReason.RecognizedSpeech)
                {
                    RecordRecognizerCallback(isFinal: true, recognizerGeneration, instanceIdHash);
                    LogSpeechResult(LogLevel.Information, "Speech final emitted.", e.Result, isFinal: true);
                    var result = e.Result;
                    TrackRecognitionCallback(() => SaveRecognizedSpeechAsync(result));
                }
                else if (e.Result.Reason == ResultReason.NoMatch)
                {
                    logger.LogInformation(
                        "Speech no match. CallId={CallId}; Reason={Reason}; Offset={Offset}; Duration={Duration}",
                        callId,
                        e.Result.Reason,
                        e.Result.OffsetInTicks,
                        e.Result.Duration);
                }
            };

            speechRecognizer.Canceled += (_, e) =>
            {
                if (e.Reason == CancellationReason.Error)
                {
                    var errorCode = e.ErrorCode.ToString();
                    var errorDetails = e.ErrorDetails;
                    logger.LogError(
                        "Speech canceled. CallId={CallId}; SpeakerId={SpeakerId}; SpeakerName={SpeakerName}; Reason={Reason}; ErrorCode={ErrorCode}; ErrorDetails={ErrorDetails}",
                        callId,
                        speakerId,
                        speakerName,
                        e.Reason,
                        errorCode,
                        errorDetails);
                    _ = RecoverFromCancellationAsync(errorCode, errorDetails);
                    return;
                }

                logger.LogWarning("Speech canceled. CallId={CallId}; SpeakerId={SpeakerId}; SpeakerName={SpeakerName}; Reason={Reason}", callId, speakerId, speakerName, e.Reason);
            };

            speechRecognizer.SessionStopped += (_, e) =>
            {
                logger.LogInformation("Speech session stopped. CallId={CallId}; SpeakerId={SpeakerId}; SpeakerName={SpeakerName}; SessionId={SessionId}", callId, speakerId, speakerName, e.SessionId);
            };
        }

        private void CreateRecognizer()
        {
            var speechConfig = SpeechConfig.FromSubscription(settings.Key, settings.Region);
            speechConfig.SpeechRecognitionLanguage = settings.RecognitionLanguage;
            speechConfig.SetProperty(
                PropertyId.Speech_SegmentationSilenceTimeoutMs,
                settings.SegmentationSilenceTimeoutMs.ToString(System.Globalization.CultureInfo.InvariantCulture));

            var audioFormat = AudioStreamFormat.GetWaveFormatPCM(16000, 16, 1);
            audioInputStream = AudioInputStream.CreatePushStream(audioFormat);
            audioConfig = AudioConfig.FromStreamInput(audioInputStream);
            var newRecognizer = new SpeechRecognizer(speechConfig, audioConfig);
            var newGeneration = Interlocked.Increment(ref pipelineGeneration);
            var newInstanceIdHash = CreateRecognizerInstanceIdHash();
            recognizer = newRecognizer;
            AttachRecognizerEvents(newRecognizer, newGeneration, newInstanceIdHash);

            lock (pipelineTelemetryGate)
            {
                recognizerInstanceIdHash = newInstanceIdHash;
                telemetryRecognizerCreated = true;
                telemetryPushStreamOpen = true;
                telemetryStarted = false;
                telemetryAcceptingFrames = false;
                lastRecognizerStartedAtUtcTicks = 0;
                lastSpeechPartialAtUtcTicks = 0;
                lastSpeechFinalAtUtcTicks = 0;
            }

            logger.LogInformation(
                "Speech recognizer created. EventAtUtc={EventAtUtc}; PipelineGeneration={PipelineGeneration}; RecognizerInstanceIdHash={RecognizerInstanceIdHash}",
                DateTimeOffset.UtcNow,
                newGeneration,
                newInstanceIdHash);
        }

        private async Task DisposeRecognizerResourcesAsync()
        {
            var currentRecognizer = recognizer;
            var currentAudioConfig = audioConfig;
            var currentAudioInputStream = audioInputStream;

            recognizer = null;
            audioConfig = null;
            audioInputStream = null;

            lock (pipelineTelemetryGate)
            {
                telemetryAcceptingFrames = false;
                telemetryStarted = false;
                telemetryRecognizerCreated = false;
                telemetryPushStreamOpen = false;
            }

            try
            {
                currentAudioInputStream?.Close();
            }
            catch (Exception ex)
            {
                logger.LogDebug(
                    ex,
                    "Speech audio input stream close failed. CallId={CallId}; SpeakerId={SpeakerId}; SpeakerName={SpeakerName}",
                    callId,
                    speakerId,
                    speakerName);
            }

            if (currentRecognizer != null)
            {
                try
                {
                    await currentRecognizer.StopContinuousRecognitionAsync().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(
                        ex,
                        "Speech recognizer stop failed while disposing resources. CallId={CallId}; SpeakerId={SpeakerId}; SpeakerName={SpeakerName}",
                        callId,
                        speakerId,
                        speakerName);
                }
                finally
                {
                    currentRecognizer.Dispose();
                }
            }

            currentAudioConfig?.Dispose();
            currentAudioInputStream?.Dispose();
        }

        private async Task RecoverFromCancellationAsync(string errorCode, string? errorDetails)
        {
            try
            {
                var throttled = IsThrottleCancellation(errorCode, errorDetails);
                await ReportSpeechStatusAsync(
                    throttled ? BotMeetingStatus.SpeechThrottled : BotMeetingStatus.SpeechError,
                    throttled
                        ? "speech recognizer throttled; reconnecting"
                        : "speech recognizer canceled; reconnecting",
                    throttled ? "azure_speech_throttled" : "azure_speech_canceled",
                    errorCode).ConfigureAwait(false);

                await ReconnectRecognizerWithBackoffAsync(throttled, errorCode).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stopCts.IsCancellationRequested)
            {
            }
            catch (Exception ex)
            {
                logger.LogError(
                    ex,
                    "Speech recognizer recovery failed. CallId={CallId}; SpeakerId={SpeakerId}; SpeakerName={SpeakerName}; ErrorCode={ErrorCode}",
                    callId,
                    speakerId,
                    speakerName,
                    errorCode);
            }
        }

        // Reconnect requests arrive as Canceled events fired from the Speech SDK and can overlap with an
        // in-flight reconnect attempt. reconnectScheduled guards a single "owner" loop; reconnectPending is
        // a dirty flag that records "a reconnect is needed" independently of who currently owns the loop.
        // Because every Interlocked operation below is a full fence, the owner is guaranteed to observe a
        // request recorded here even if it arrives between the owner finishing its work and releasing
        // reconnectScheduled - so a Canceled event racing with a reconnect can no longer be silently dropped.
        private async Task ReconnectRecognizerWithBackoffAsync(bool throttled, string errorCode)
        {
            Interlocked.Exchange(ref reconnectPending, 1);

            while (true)
            {
                if (Interlocked.CompareExchange(ref reconnectScheduled, 1, 0) != 0)
                {
                    // Another call already owns the reconnect loop. It will re-check reconnectPending after
                    // releasing ownership, so this request is not lost.
                    return;
                }

                if (Interlocked.Exchange(ref reconnectPending, 0) == 0)
                {
                    // We became the owner, but there is nothing new to process (our own earlier iteration
                    // already handled the request that led here). Release ownership.
                    Interlocked.Exchange(ref reconnectScheduled, 0);
                    if (Volatile.Read(ref reconnectPending) == 0)
                    {
                        return;
                    }

                    // A new request snuck in during the tiny window above; try to become owner again.
                    continue;
                }

                var reconnectedSuccessfully = false;
                try
                {
                    while (Volatile.Read(ref started) == 1 && Volatile.Read(ref stopping) == 0)
                    {
                        var attempt = Interlocked.Increment(ref reconnectAttempt);
                        var delay = GetReconnectDelay(attempt, throttled);
                        logger.LogWarning(
                            "Speech recognizer reconnect scheduled. CallId={CallId}; SpeakerId={SpeakerId}; SpeakerName={SpeakerName}; ErrorCode={ErrorCode}; Attempt={Attempt}; DelayMs={DelayMs}",
                            callId,
                            speakerId,
                            speakerName,
                            errorCode,
                            attempt,
                            delay.TotalMilliseconds);

                        await Task.Delay(delay, stopCts.Token).ConfigureAwait(false);
                        if (Volatile.Read(ref started) != 1 || Volatile.Read(ref stopping) != 0)
                        {
                            // Stopping: abandon any pending request entirely, we're shutting down.
                            return;
                        }

                        try
                        {
                            var reconnectedThisAttempt = false;
                            await lifecycleLock.WaitAsync(stopCts.Token).ConfigureAwait(false);
                            try
                            {
                                if (Volatile.Read(ref started) != 1 || Volatile.Read(ref stopping) != 0)
                                {
                                    return;
                                }

                                MarkAcceptingFrames(false);
                                await DisposeRecognizerResourcesAsync().ConfigureAwait(false);
                                CreateRecognizer();
                                await recognizer!.StartContinuousRecognitionAsync().ConfigureAwait(false);

                                if (Volatile.Read(ref stopping) != 0)
                                {
                                    // StopAsync raced us while we were (re)starting the recognizer. Don't
                                    // claim we're recording; tear down what we just started and let
                                    // StopAsync's own shutdown handling stand.
                                    await DisposeRecognizerResourcesAsync().ConfigureAwait(false);
                                    return;
                                }

                                MarkRecognizerStarted();
                                reconnectedThisAttempt = true;

                                logger.LogInformation(
                                    "Speech recognizer reconnected. EventAtUtc={EventAtUtc}; PipelineGeneration={PipelineGeneration}; RecognizerInstanceIdHash={RecognizerInstanceIdHash}; CallId={CallId}; SpeakerId={SpeakerId}; SpeakerName={SpeakerName}; Attempt={Attempt}",
                                    DateTimeOffset.UtcNow,
                                    Snapshot.PipelineGeneration,
                                    Snapshot.RecognizerInstanceIdHash,
                                    callId,
                                    speakerId,
                                    speakerName,
                                    attempt);
                            }
                            finally
                            {
                                lifecycleLock.Release();
                            }

                            if (reconnectedThisAttempt)
                            {
                                await ReportSpeechStatusAsync(
                                    BotMeetingStatus.Recording,
                                    "speech recognizer reconnected",
                                    "azure_speech_reconnected",
                                    errorCode).ConfigureAwait(false);

                                // Reconnected successfully. Break out of the retry loop (instead of
                                // returning) so the outer ownership loop below re-checks reconnectPending
                                // for any request that arrived while we were reconnecting.
                                reconnectedSuccessfully = true;
                                break;
                            }
                        }
                        catch (OperationCanceledException) when (stopCts.IsCancellationRequested)
                        {
                            return;
                        }
                        catch (Exception ex)
                        {
                            MarkAcceptingFrames(false);
                            logger.LogError(
                                ex,
                                "Speech recognizer reconnect attempt failed. CallId={CallId}; SpeakerId={SpeakerId}; SpeakerName={SpeakerName}; Attempt={Attempt}",
                                callId,
                                speakerId,
                                speakerName,
                                attempt);
                            await ReportSpeechStatusAsync(
                                throttled ? BotMeetingStatus.SpeechThrottled : BotMeetingStatus.SpeechError,
                                "speech recognizer reconnect failed; retrying",
                                "azure_speech_reconnect_failed",
                                ex.GetType().Name).ConfigureAwait(false);
                        }
                    }

                    if (!reconnectedSuccessfully)
                    {
                        // The retry loop ended because started/stopping flipped (shutting down), not because
                        // we succeeded. Discard any pending request and exit entirely.
                        return;
                    }
                }
                finally
                {
                    Interlocked.Exchange(ref reconnectScheduled, 0);
                }
            }
        }

        private async Task ReportSpeechStatusSafelyAsync(string status, string message, string failedReason, string? errorCode)
        {
            try
            {
                await ReportSpeechStatusAsync(status, message, failedReason, errorCode).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                logger.LogWarning(
                    ex,
                    "Speech status report failed. CallId={CallId}; SpeakerId={SpeakerId}; SpeakerName={SpeakerName}; Status={Status}",
                    callId,
                    speakerId,
                    speakerName,
                    status);
            }
        }

        private Task ReportSpeechStatusAsync(string status, string message, string failedReason, string? errorCode)
        {
            if (speechStatusSink != null)
            {
                // Route recording/speech_throttled/speech_error through the session-level aggregator
                // instead of reporting directly, so concurrent per-speaker (and mixed-fallback)
                // SpeechService instances don't overwrite each other's status with Last-Write-Wins PATCHes.
                return speechStatusSink.ReportAsync(statusInstanceId, status, message, failedReason, errorCode);
            }

            return statusReporter.ReportAsync(
                sessionId,
                status,
                message,
                callId,
                CancellationToken.None,
                failedReason: failedReason,
                errorCode: errorCode,
                source: "speech_pipeline");
        }

        private void ResetReconnectAttempt()
        {
            if (Volatile.Read(ref reconnectAttempt) != 0)
            {
                Interlocked.Exchange(ref reconnectAttempt, 0);
            }
        }

        internal static bool IsThrottleCancellation(string? errorCode, string? errorDetails)
        {
            return ContainsOrdinalIgnoreCase(errorCode, "TooManyRequests")
                || ContainsOrdinalIgnoreCase(errorDetails, "TooManyRequests")
                || ContainsOrdinalIgnoreCase(errorDetails, "4429");
        }

        private static bool ContainsOrdinalIgnoreCase(string? value, string marker)
        {
            return value?.IndexOf(marker, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        internal static TimeSpan GetReconnectDelay(int attempt, bool throttled)
        {
            var baseSeconds = throttled ? 2 : 1;
            var exponent = Math.Min(Math.Max(attempt - 1, 0), 5);
            var seconds = Math.Min(MaxReconnectDelay.TotalSeconds, baseSeconds * Math.Pow(2, exponent));
            return TimeSpan.FromSeconds(seconds);
        }

        private void LogSpeechResult(LogLevel level, string message, SpeechRecognitionResult result, bool isFinal)
        {
            if (settings.LogTranscripts)
            {
                logger.Log(
                    level,
                    "{Message} FiredAtUtc={FiredAtUtc}; SessionId={SessionId}; CallId={CallId}; SpeakerId={SpeakerId}; SpeakerName={SpeakerName}; IsFinal={IsFinal}; Text={Text}; TextLength={TextLength}; Offset={Offset}; Duration={Duration}; Reason={Reason}",
                    message,
                    DateTimeOffset.UtcNow,
                    sessionId,
                    callId,
                    speakerId,
                    speakerName,
                    isFinal,
                    result.Text,
                    result.Text?.Length ?? 0,
                    result.OffsetInTicks,
                    result.Duration,
                    result.Reason);
                return;
            }

            logger.Log(
                level,
                "{Message} FiredAtUtc={FiredAtUtc}; SessionId={SessionId}; CallId={CallId}; SpeakerId={SpeakerId}; SpeakerName={SpeakerName}; IsFinal={IsFinal}; TextLength={TextLength}; Offset={Offset}; Duration={Duration}; Reason={Reason}",
                message,
                DateTimeOffset.UtcNow,
                sessionId,
                callId,
                speakerId,
                speakerName,
                isFinal,
                result.Text?.Length ?? 0,
                result.OffsetInTicks,
                result.Duration,
                result.Reason);
        }

        private async Task ForwardRecognizingSpeechAsync(SpeechRecognitionResult result)
        {
            if (string.IsNullOrWhiteSpace(result.Text))
            {
                return;
            }

            try
            {
                var partial = new TranscriptSegment
                {
                    SessionId = sessionId,
                    CallId = callId,
                    SpeakerId = speakerId,
                    SpeakerName = speakerName,
                    RecognizedAtUtc = DateTimeOffset.UtcNow.ToString("O"),
                    OffsetTicks = result.OffsetInTicks,
                    DurationTicks = result.Duration.Ticks,
                    Text = result.Text,
                    IsFinal = false,
                };

                await transcriptForwarder.ForwardAsync(partial, 0, isFinal: false).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                logger.LogWarning(
                    ex,
                    "Failed to forward partial transcript. SessionId={SessionId}; CallId={CallId}; SpeakerId={SpeakerId}; SpeakerName={SpeakerName}; TextLength={TextLength}; Offset={Offset}; Duration={Duration}",
                    sessionId,
                    callId,
                    speakerId,
                    speakerName,
                    result.Text?.Length ?? 0,
                    result.OffsetInTicks,
                    result.Duration);
            }
        }

        private async Task SaveRecognizedSpeechAsync(SpeechRecognitionResult result)
        {
            var now = DateTimeOffset.UtcNow;
            Interlocked.Exchange(ref lastFinalTranscriptAtUtcTicks, now.Ticks);

            if (string.IsNullOrWhiteSpace(result.Text))
            {
                logger.LogInformation(
                    "Speech recognized. SessionId={SessionId}; CallId={CallId}; SpeakerId={SpeakerId}; SpeakerName={SpeakerName}; SequenceNo={SequenceNo}; TextLength={TextLength}; EmptyTextSkipped={EmptyTextSkipped}",
                    sessionId,
                    callId,
                    speakerId,
                    speakerName,
                    null,
                    result.Text?.Length ?? 0,
                    true);
                return;
            }

            Interlocked.Exchange(ref lastNonEmptyTranscriptAtUtcTicks, now.Ticks);

            try
            {
                var segment = new TranscriptSegment
                {
                    SessionId = sessionId,
                    CallId = callId,
                    SpeakerId = speakerId,
                    SpeakerName = speakerName,
                    RecognizedAtUtc = DateTimeOffset.UtcNow.ToString("O"),
                    OffsetTicks = result.OffsetInTicks,
                    DurationTicks = result.Duration.Ticks,
                    Text = result.Text,
                    IsFinal = true,
                };

                var sequenceNo = await transcriptSequenceProvider.NextSequenceNoAsync(callId).ConfigureAwait(false);
                logger.LogInformation(
                    "Speech recognized. SessionId={SessionId}; CallId={CallId}; SequenceNo={SequenceNo}; SpeakerId={SpeakerId}; SpeakerName={SpeakerName}; TextLength={TextLength}; EmptyTextSkipped={EmptyTextSkipped}",
                    sessionId,
                    callId,
                    sequenceNo,
                    speakerId,
                    speakerName,
                    result.Text.Length,
                    false);
                logger.LogInformation(
                    "Transcript final sequence assigned. SessionId={SessionId}; CallId={CallId}; SpeakerId={SpeakerId}; SpeakerName={SpeakerName}; SequenceNo={SequenceNo}",
                    sessionId,
                    callId,
                    speakerId,
                    speakerName,
                    sequenceNo);
                await transcriptForwarder.ForwardAsync(segment, sequenceNo).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to forward recognized transcript. SessionId={SessionId}; CallId={CallId}", sessionId, callId);
            }
        }

        private void MarkRecognizerStarted()
        {
            var now = DateTimeOffset.UtcNow;
            string? instanceIdHash;
            long generation;
            lock (pipelineTelemetryGate)
            {
                telemetryStarted = true;
                telemetryAcceptingFrames = true;
                lastRecognizerStartedAtUtcTicks = now.Ticks;
                instanceIdHash = recognizerInstanceIdHash;
                generation = pipelineGeneration;
            }

            Volatile.Write(ref acceptingFrames, 1);
            logger.LogInformation(
                "Speech recognizer accepting audio. EventAtUtc={EventAtUtc}; PipelineGeneration={PipelineGeneration}; RecognizerInstanceIdHash={RecognizerInstanceIdHash}",
                now,
                generation,
                instanceIdHash);
        }

        private void MarkAcceptingFrames(bool value)
        {
            Volatile.Write(ref acceptingFrames, value ? 1 : 0);
            lock (pipelineTelemetryGate)
            {
                telemetryAcceptingFrames = value;
            }
        }

        private void RecordRecognizerCallback(bool isFinal, long recognizerGeneration, string instanceIdHash)
        {
            var now = DateTimeOffset.UtcNow;
            bool isCurrentRecognizer;
            bool invariantSatisfied;

            lock (pipelineTelemetryGate)
            {
                isCurrentRecognizer = pipelineGeneration == recognizerGeneration
                    && string.Equals(recognizerInstanceIdHash, instanceIdHash, StringComparison.Ordinal);
                invariantSatisfied = isCurrentRecognizer
                    && SpeechPipelineSnapshot.CallbackStateInvariantSatisfied(
                        telemetryStarted,
                        telemetryRecognizerCreated,
                        telemetryPushStreamOpen);

                if (isCurrentRecognizer)
                {
                    if (isFinal)
                    {
                        lastSpeechFinalAtUtcTicks = now.Ticks;
                    }
                    else
                    {
                        lastSpeechPartialAtUtcTicks = now.Ticks;
                    }
                }
            }

            logger.Log(
                isFinal ? LogLevel.Information : LogLevel.Debug,
                "Speech recognizer callback observed. EventAtUtc={EventAtUtc}; CallbackType={CallbackType}; PipelineGeneration={PipelineGeneration}; RecognizerInstanceIdHash={RecognizerInstanceIdHash}; IsCurrentRecognizer={IsCurrentRecognizer}; PipelineInvariantSatisfied={PipelineInvariantSatisfied}",
                now,
                isFinal ? "final" : "partial",
                recognizerGeneration,
                instanceIdHash,
                isCurrentRecognizer,
                invariantSatisfied);

            if (!invariantSatisfied)
            {
                var build = BuildFingerprint.Current;
                logger.LogWarning(
                    "Speech callback/pipeline invariant mismatch. EventAtUtc={EventAtUtc}; CallbackType={CallbackType}; PipelineGeneration={PipelineGeneration}; RecognizerInstanceIdHash={RecognizerInstanceIdHash}; IsCurrentRecognizer={IsCurrentRecognizer}; BuildVersion={BuildVersion}; GitCommitSha={GitCommitSha}; BuildTimestamp={BuildTimestamp}",
                    now,
                    isFinal ? "final" : "partial",
                    recognizerGeneration,
                    instanceIdHash,
                    isCurrentRecognizer,
                    build.BuildVersion,
                    build.GitCommitSha,
                    build.BuildTimestamp);
            }
        }

        private static string CreateRecognizerInstanceIdHash()
        {
            var sourceBytes = System.Text.Encoding.UTF8.GetBytes(Guid.NewGuid().ToString("N"));
            var hash = System.Security.Cryptography.SHA256.HashData(sourceBytes);
            return Convert.ToHexString(hash.AsSpan(0, 8)).ToLowerInvariant();
        }

        private static string? NormalizeSpeakerName(string? value)
        {
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }

        private static DateTimeOffset? TicksToUtc(long ticks)
        {
            return ticks == 0 ? (DateTimeOffset?)null : new DateTimeOffset(ticks, TimeSpan.Zero);
        }
    }
}
