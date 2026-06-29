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
        private readonly ITranscriptRepository transcriptRepository;
        private readonly ITranscriptForwarder transcriptForwarder;
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

        public SpeechService(
            string callId,
            AppSettings appSettings,
            ILogger logger,
            ITranscriptRepository transcriptRepository,
            ITranscriptForwarder transcriptForwarder,
            string? sessionId = null,
            string? speakerId = null,
            string? speakerName = null)
        {
            this.callId = callId;
            this.logger = logger;
            this.transcriptRepository = transcriptRepository;
            this.transcriptForwarder = transcriptForwarder;
            this.sessionId = sessionId;
            this.speakerId = speakerId;
            this.speakerName = NormalizeSpeakerName(speakerName);
            settings = SpeechTranscriptionSettings.FromAppSettings(appSettings);
            audioQueue = new BoundedAudioFrameQueue(settings.AudioQueueCapacity);
        }

        public long DroppedFrames => audioQueue.DroppedFrames;

        public bool IsStarted => Volatile.Read(ref started) == 1;

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

        public SpeechPipelineSnapshot Snapshot => new SpeechPipelineSnapshot(
            serviceAvailable: true,
            started: Volatile.Read(ref started) == 1,
            acceptingFrames: Volatile.Read(ref acceptingFrames) == 1,
            recognizerCreated: recognizer != null,
            pushStreamOpen: audioInputStream != null,
            droppedFrames: audioQueue.DroppedFrames);

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

                var speechConfig = SpeechConfig.FromSubscription(settings.Key, settings.Region);
                speechConfig.SpeechRecognitionLanguage = settings.RecognitionLanguage;
                speechConfig.SetProperty(
                    PropertyId.Speech_SegmentationSilenceTimeoutMs,
                    settings.SegmentationSilenceTimeoutMs.ToString(System.Globalization.CultureInfo.InvariantCulture));

                var audioFormat = AudioStreamFormat.GetWaveFormatPCM(16000, 16, 1);
                audioInputStream = AudioInputStream.CreatePushStream(audioFormat);
                audioConfig = AudioConfig.FromStreamInput(audioInputStream);
                recognizer = new SpeechRecognizer(speechConfig, audioConfig);

                AttachRecognizerEvents(recognizer);

                queuePumpTask = Task.Run(() => PumpAudioAsync(stopCts.Token));
                await recognizer.StartContinuousRecognitionAsync().ConfigureAwait(false);
                Volatile.Write(ref acceptingFrames, 1);

                logger.LogInformation("Speech recognition session started. CallId={CallId}; SpeakerId={SpeakerId}; SpeakerName={SpeakerName}", callId, speakerId, speakerName);
            }
            catch
            {
                Volatile.Write(ref acceptingFrames, 0);
                Volatile.Write(ref started, 0);
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

            Volatile.Write(ref acceptingFrames, 0);
            audioQueue.Complete();

            await lifecycleLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                logger.LogInformation("Stopping Azure Speech transcription. CallId={CallId}; SpeakerId={SpeakerId}; SpeakerName={SpeakerName}", callId, speakerId, speakerName);

                if (queuePumpTask != null)
                {
                    await queuePumpTask.ConfigureAwait(false);
                }

                audioInputStream?.Close();

                if (recognizer != null)
                {
                    await recognizer.StopContinuousRecognitionAsync().ConfigureAwait(false);
                    recognizer.Dispose();
                    recognizer = null;
                }

                audioConfig?.Dispose();
                audioConfig = null;
                audioInputStream?.Dispose();
                audioInputStream = null;

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
                stopCts.Cancel();
                Volatile.Write(ref started, 0);
                lifecycleLock.Release();
            }
        }

        public async ValueTask DisposeAsync()
        {
            await StopAsync().ConfigureAwait(false);
            stopCts.Dispose();
            lifecycleLock.Dispose();
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
                    audioInputStream?.Write(frame);
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

        private void AttachRecognizerEvents(SpeechRecognizer speechRecognizer)
        {
            speechRecognizer.SessionStarted += (_, e) =>
            {
                logger.LogInformation("Speech session started. CallId={CallId}; SpeakerId={SpeakerId}; SpeakerName={SpeakerName}; SessionId={SessionId}", callId, speakerId, speakerName, e.SessionId);
            };

            speechRecognizer.Recognizing += (_, e) =>
            {
                LogSpeechResult(LogLevel.Debug, "Speech partial emitted.", e.Result, isFinal: false);
                _ = ForwardRecognizingSpeechAsync(e.Result);
            };

            speechRecognizer.Recognized += (_, e) =>
            {
                if (e.Result.Reason == ResultReason.RecognizedSpeech)
                {
                    LogSpeechResult(LogLevel.Information, "Speech final emitted.", e.Result, isFinal: true);
                    _ = SaveRecognizedSpeechAsync(e.Result);
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
                    logger.LogError(
                        "Speech canceled. CallId={CallId}; Reason={Reason}; ErrorCode={ErrorCode}; ErrorDetails={ErrorDetails}",
                        callId,
                        e.Reason,
                        e.ErrorCode,
                        e.ErrorDetails);
                    return;
                }

                logger.LogWarning("Speech canceled. CallId={CallId}; Reason={Reason}", callId, e.Reason);
            };

            speechRecognizer.SessionStopped += (_, e) =>
            {
                logger.LogInformation("Speech session stopped. CallId={CallId}; SpeakerId={SpeakerId}; SpeakerName={SpeakerName}; SessionId={SessionId}", callId, speakerId, speakerName, e.SessionId);
            };
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

                var sequenceNo = await transcriptRepository.SaveAsync(segment).ConfigureAwait(false);
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
                    "Transcript saved to SQLite. SessionId={SessionId}; CallId={CallId}; SpeakerId={SpeakerId}; SpeakerName={SpeakerName}; SequenceNo={SequenceNo}; DatabasePath={DatabasePath}",
                    sessionId,
                    callId,
                    speakerId,
                    speakerName,
                    sequenceNo,
                    transcriptRepository.DatabasePath);
                await transcriptForwarder.ForwardAsync(segment, sequenceNo).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to save transcript to SQLite. SessionId={SessionId}; CallId={CallId}", sessionId, callId);
            }
        }

        private static string? NormalizeSpeakerName(string? value)
        {
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }
    }
}
