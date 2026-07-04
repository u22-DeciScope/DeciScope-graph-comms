// ***********************************************************************
// Assembly         : EchoBot.Services
// Author           : JasonTheDeveloper
// Created          : 09-07-2020
//
// Last Modified By : bcage29
// Last Modified On : 10-17-2023
// ***********************************************************************
// <copyright file="BotMediaStream.cs" company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license.
// </copyright>
// <summary>The bot media stream.</summary>
// ***********************************************************************-
using EchoBot.Media;
using EchoBot.Services;
using EchoBot.Util;
using Microsoft.Graph.Communications.Calls;
using Microsoft.Graph.Communications.Calls.Media;
using Microsoft.Graph.Communications.Common;
using Microsoft.Graph.Communications.Common.Telemetry;
using Microsoft.Skype.Bots.Media;
using Microsoft.Skype.Internal.Media.Services.Common;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;

namespace EchoBot.Bot
{
    /// <summary>
    /// Class responsible for streaming audio and video.
    /// </summary>
    public class BotMediaStream : ObjectRootDisposable
    {
        private AppSettings _settings;

        /// <summary>
        /// The participants
        /// </summary>
        internal List<IParticipant> participants;

        /// <summary>
        /// The audio socket
        /// </summary>
        private readonly IAudioSocket _audioSocket = null!;
        /// <summary>
        /// The media stream
        /// </summary>
        private readonly ILogger _logger;
        private AudioVideoFramePlayer? audioVideoFramePlayer;
        private readonly TaskCompletionSource<bool> audioSendStatusActive;
        private readonly TaskCompletionSource<bool> startVideoPlayerCompleted;
        private AudioVideoFramePlayerSettings? audioVideoFramePlayerSettings;
        private List<AudioMediaBuffer> audioMediaBuffers = new List<AudioMediaBuffer>();
        private readonly SpeechService? _languageService;
        private readonly AppSettings appSettings;
        private readonly ITranscriptSequenceProvider transcriptSequenceProvider;
        private readonly ITranscriptForwarder transcriptForwarder;
        private readonly IBotMeetingStatusReporter statusReporter;
        private readonly SemaphoreSlim mixedSpeechFallbackStopLock = new SemaphoreSlim(1, 1);
        private readonly ConcurrentDictionary<string, SpeakerInfo> speakersBySourceId = new ConcurrentDictionary<string, SpeakerInfo>(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<string, SpeechService> speechServicesBySpeakerId = new ConcurrentDictionary<string, SpeechService>(StringComparer.OrdinalIgnoreCase);
        private readonly string callId;
        private string? sessionId;
        private CallOrigin origin;
        private readonly MediaDiagnostics diagnostics;
        private MediaSendStatus audioSendStatus = MediaSendStatus.Inactive;
        private int nonZeroAudioDetected;
        private int audioFrameSpeechStartAttempted;
        private int unmixedAudioObserved;
        private int mixedSpeechFallbackStoppedForUnmixedAudio;

        /// <summary>
        /// 20ms of PCM16 mono 16kHz silence. Teams delivers audio in 20ms frames,
        /// so one frame of this keeps a recognizer's audio stream advancing in real time.
        /// </summary>
        private static readonly byte[] SilencePcmFrame = new byte[640];

        /// <summary>
        /// Initializes a new instance of the <see cref="BotMediaStream" /> class.
        /// </summary>
        /// <param name="mediaSession">The media session.</param>
        /// <param name="callId">The call identity</param>
        /// <param name="graphLogger">The Graph logger.</param>
        /// <param name="logger">The logger.</param>
        /// <param name="settings">Azure settings</param>
        /// <exception cref="InvalidOperationException">A mediaSession needs to have at least an audioSocket</exception>
        public BotMediaStream(
            ILocalMediaSession mediaSession,
            string callId,
            IGraphLogger graphLogger,
            ILogger logger,
            AppSettings settings,
            ITranscriptSequenceProvider transcriptSequenceProvider,
            ITranscriptForwarder transcriptForwarder,
            IBotMeetingStatusReporter statusReporter,
            string? sessionId = null,
            CallOrigin origin = CallOrigin.OutboundJoin
        )
            : base(graphLogger)
        {
            ArgumentVerifier.ThrowOnNullArgument(mediaSession, nameof(mediaSession));
            ArgumentVerifier.ThrowOnNullArgument(logger, nameof(logger));
            ArgumentVerifier.ThrowOnNullArgument(settings, nameof(settings));

            _settings = settings;
            _logger = logger;
            this.callId = callId;
            this.sessionId = sessionId;
            this.origin = origin;
            this.diagnostics = new MediaDiagnostics(callId, _settings.UseSpeechService);
            this.appSettings = settings;
            this.transcriptSequenceProvider = transcriptSequenceProvider;
            this.transcriptForwarder = transcriptForwarder;
            this.statusReporter = statusReporter;

            _logger.LogInformation(
                "Bot media mode: {MediaMode}. CallId={CallId}; SessionId={SessionId}; Origin={Origin}; UseSpeechService={UseSpeechService}",
                this.diagnostics.ModeName,
                this.callId,
                this.sessionId,
                this.origin,
                _settings.UseSpeechService);

            this.participants = new List<IParticipant>();

            this.audioSendStatusActive = new TaskCompletionSource<bool>();
            this.startVideoPlayerCompleted = new TaskCompletionSource<bool>();

            // Subscribe to the audio media.
            var audioSocket = mediaSession.AudioSocket;
            if (audioSocket == null)
            {
                _logger.LogWarning("AudioSocket was not available. CallId={CallId}", this.callId);
                throw new InvalidOperationException("A mediaSession needs to have at least an audioSocket");
            }

            this._audioSocket = audioSocket;

            _logger.LogInformation("AudioSocket initialized. CallId={CallId}; HasAudioSocket={HasAudioSocket}", this.callId, this._audioSocket != null);

            if (_settings.UseSpeechService)
            {
                _languageService = new SpeechService(this.callId, _settings, _logger, transcriptSequenceProvider, transcriptForwarder, statusReporter, sessionId);
                this.startVideoPlayerCompleted.TrySetResult(true);
            }
            else
            {
                var ignoreTask = this.StartAudioVideoFramePlayerAsync().ForgetAndLogExceptionAsync(this.GraphLogger, "Failed to start the player");
            }

            audioSocket.AudioSendStatusChanged += OnAudioSendStatusChanged;
            _logger.LogDebug("AudioSendStatusChanged subscribed. CallId={CallId}; Subscribed={Subscribed}", this.callId, true);

            audioSocket.AudioMediaReceived += this.OnAudioMediaReceived;
            _logger.LogDebug("AudioMediaReceived subscribed. CallId={CallId}; Subscribed={Subscribed}", this.callId, true);

            _logger.LogInformation(
                "BotMediaStream initialized. CallId={CallId}; SessionId={SessionId}; Origin={Origin}; HasAudioSocket={HasAudioSocket}; MediaMode={MediaMode}; SpeechServiceAvailable={SpeechServiceAvailable}",
                this.callId,
                this.sessionId,
                this.origin,
                this._audioSocket != null,
                this.diagnostics.ModeName,
                this._languageService != null);
        }

        /// <summary>
        /// Gets the participants.
        /// </summary>
        /// <returns>List&lt;IParticipant&gt;.</returns>
        public List<IParticipant> GetParticipants()
        {
            return participants;
        }

        public long ReceivedAudioFrameCount => this.diagnostics.ReceivedFrames;

        public long SentAudioFrameCount => this.diagnostics.SentFrames;

        public string MediaMode => this.diagnostics.ModeName;

        public SpeechPipelineSnapshot SpeechPipelineSnapshot => _languageService?.Snapshot ?? SpeechPipelineSnapshot.Unavailable();

        public void RegisterParticipantSpeaker(string sourceId, string? displayName, string participantId)
        {
            if (string.IsNullOrWhiteSpace(sourceId))
            {
                return;
            }

            var speaker = new SpeakerInfo(sourceId.Trim(), NormalizeSpeakerName(displayName), participantId);
            speakersBySourceId.AddOrUpdate(speaker.SourceId, speaker, (_, _) => speaker);

            if (speechServicesBySpeakerId.TryGetValue(speaker.SourceId, out var service))
            {
                service.SetSpeakerName(speaker.DisplayName);
            }

            _logger.LogInformation(
                "Participant speaker mapped. CallId={CallId}; ParticipantId={ParticipantId}; SpeakerId={SpeakerId}; SpeakerName={SpeakerName}",
                this.callId,
                participantId,
                speaker.SourceId,
                speaker.DisplayName);
        }

        public void UnregisterParticipantSpeaker(string sourceId)
        {
            if (string.IsNullOrWhiteSpace(sourceId))
            {
                return;
            }

            speakersBySourceId.TryRemove(sourceId.Trim(), out _);
        }

        public void UpdateContext(string? sessionId, CallOrigin origin)
        {
            if (!string.IsNullOrWhiteSpace(sessionId))
            {
                this.sessionId = sessionId;
                _languageService?.SetSessionId(sessionId);
                foreach (var service in speechServicesBySpeakerId.Values)
                {
                    service.SetSessionId(sessionId);
                }
            }

            this.origin = origin;

            _logger.LogInformation(
                "BotMediaStream context updated. CallId={CallId}; SessionId={SessionId}; Origin={Origin}",
                this.callId,
                this.sessionId,
                this.origin);
        }

        public Task<bool> StartSpeechTranscriptionAsync(CancellationToken cancellationToken = default)
        {
            if (_languageService == null)
            {
                return Task.FromResult(false);
            }

            return StartSpeechTranscriptionCoreAsync(cancellationToken);
        }

        public Task StopSpeechTranscriptionAsync(CancellationToken cancellationToken = default)
        {
            if (_languageService == null)
            {
                return Task.CompletedTask;
            }

            return StopAllSpeechTranscriptionAsync(cancellationToken);
        }

        /// <summary>
        /// Shut down.
        /// </summary>
        /// <returns><see cref="Task" />.</returns>
        public async Task ShutdownAsync()
        {
            if (!this.diagnostics.TryBeginShutdown())
            {
                _logger.LogDebug("BotMediaStream shutdown already requested. CallId={CallId}", this.callId);
                return;
            }

            _logger.LogInformation(
                "BotMediaStream shutdown starting. CallId={CallId}; ReceivedFrames={ReceivedFrames}; SentFrames={SentFrames}",
                this.callId,
                this.diagnostics.ReceivedFrames,
                this.diagnostics.SentFrames);

            try
            {
                await this.startVideoPlayerCompleted.Task.ConfigureAwait(false);
                if (this._languageService != null)
                {
                    await this._languageService.StopAsync().ConfigureAwait(false);
                }

                foreach (var service in speechServicesBySpeakerId.Values)
                {
                    await service.StopAsync().ConfigureAwait(false);
                }

                // unsubscribe
                if (this._audioSocket != null)
                {
                    this._audioSocket.AudioSendStatusChanged -= this.OnAudioSendStatusChanged;
                    this._audioSocket.AudioMediaReceived -= this.OnAudioMediaReceived;
                }

                // shutting down the players
                if (this.audioVideoFramePlayer != null)
                {
                    await this.audioVideoFramePlayer.ShutdownAsync().ConfigureAwait(false);
                }

                // make sure all the audio and video buffers are disposed, it can happen that,
                // the buffers were not enqueued but the call was disposed if the caller hangs up quickly
                foreach (var audioMediaBuffer in this.audioMediaBuffers)
                {
                    audioMediaBuffer.Dispose();
                }

                _logger.LogInformation(
                    "BotMediaStream shutdown completed. CallId={CallId}; ReceivedFrames={ReceivedFrames}; SentFrames={SentFrames}; DisposedAudioBuffers={DisposedAudioBuffers}",
                    this.callId,
                    this.diagnostics.ReceivedFrames,
                    this.diagnostics.SentFrames,
                    this.audioMediaBuffers.Count);

                this.audioMediaBuffers.Clear();
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "BotMediaStream shutdown failed. CallId={CallId}; ReceivedFrames={ReceivedFrames}; SentFrames={SentFrames}",
                    this.callId,
                    this.diagnostics.ReceivedFrames,
                    this.diagnostics.SentFrames);
                throw;
            }
        }

        /// <summary>
        /// Initialize AV frame player.
        /// </summary>
        /// <returns>Task denoting creation of the player with initial frames enqueued.</returns>
        private async Task StartAudioVideoFramePlayerAsync()
        {
            try
            {
                _logger.LogInformation("Creating audio video frame player. CallId={CallId}", this.callId);
                this.audioVideoFramePlayerSettings =
                    new AudioVideoFramePlayerSettings(new AudioSettings(20), new VideoSettings(), 1000);
                this.audioVideoFramePlayer = new AudioVideoFramePlayer(
                    (AudioSocket)_audioSocket,
                    null,
                    this.audioVideoFramePlayerSettings);

                _logger.LogInformation("Audio video frame player created. CallId={CallId}", this.callId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create the audioVideoFramePlayer. CallId={CallId}", this.callId);
            }
            finally
            {
                this.startVideoPlayerCompleted.TrySetResult(true);
            }
        }

        /// <summary>
        /// Callback for informational updates from the media plaform about audio status changes.
        /// Once the status becomes active, audio can be loopbacked.
        /// </summary>
        /// <param name="sender">The audio socket.</param>
        /// <param name="e">Event arguments.</param>
        private void OnAudioSendStatusChanged(object? sender, AudioSendStatusChangedEventArgs e)
        {
            var oldStatus = this.audioSendStatus;
            this.audioSendStatus = e.MediaSendStatus;

            _logger.LogDebug(
                "Audio send status changed. CallId={CallId}; OldStatus={OldStatus}; NewStatus={NewStatus}",
                this.callId,
                oldStatus,
                e.MediaSendStatus);

            if (e.MediaSendStatus == MediaSendStatus.Active)
            {
                this.audioSendStatusActive.TrySetResult(true);
            }
            else
            {
                _logger.LogWarning("Audio send status is not active. CallId={CallId}; Status={Status}", this.callId, e.MediaSendStatus);
            }
        }

        /// <summary>
        /// Receive audio from subscribed participant.
        /// </summary>
        /// <param name="sender">The sender.</param>
        /// <param name="e">The audio media received arguments.</param>
        private async void OnAudioMediaReceived(object? sender, AudioMediaReceivedEventArgs e)
        {
            try
            {
                var receivedFrame = this.diagnostics.RecordReceivedFrame(e.Buffer.Length, e.Buffer.Timestamp);
                if (receivedFrame.ShouldLog)
                {
                    _logger.LogInformation(
                        "Audio frames received. CallId={CallId}; TotalFrames={TotalFrames}; BufferLength={BufferLength}; Timestamp={Timestamp}; UseSpeechService={UseSpeechService}; MediaMode={MediaMode}",
                        receivedFrame.CallId,
                        receivedFrame.TotalFrames,
                        receivedFrame.BufferLength,
                        receivedFrame.Timestamp,
                        _settings.UseSpeechService,
                        receivedFrame.MediaMode);
                }

                if (!startVideoPlayerCompleted.Task.IsCompleted)
                {
                    _logger.LogDebug(
                        "Audio frame received before audio video frame player was ready. CallId={CallId}; TotalFrames={TotalFrames}",
                        this.callId,
                        receivedFrame.TotalFrames);
                    return;
                }

                if (_languageService != null)
                {
                    if (await TryProcessUnmixedAudioAsync(e.Buffer, receivedFrame.TotalFrames).ConfigureAwait(false))
                    {
                        return;
                    }

                    var length = e.Buffer.Length;
                    if (length > 0)
                    {
                        var buffer = CopyAudioBuffer(e.Buffer);
                        var level = PcmAudioLevelCalculator.Calculate(buffer);
                        if (Volatile.Read(ref nonZeroAudioDetected) == 0 && level.PeakAmplitude > 0)
                        {
                            Volatile.Write(ref nonZeroAudioDetected, 1);
                            _logger.LogInformation(
                                "First non-zero audio detected. CallId={CallId}; SessionId={SessionId}; Origin={Origin}; TotalFrames={TotalFrames}; BufferLength={BufferLength}; PeakAmplitude={PeakAmplitude}; RmsAmplitude={RmsAmplitude}",
                                this.callId,
                                this.sessionId,
                                this.origin,
                                receivedFrame.TotalFrames,
                                buffer.Length,
                                level.PeakAmplitude,
                                level.RmsAmplitude);
                        }

                        var preEnqueueSnapshot = this.SpeechPipelineSnapshot;
                        if (!preEnqueueSnapshot.Ready
                            && Volatile.Read(ref mixedSpeechFallbackStoppedForUnmixedAudio) == 0
                            && this.origin != CallOrigin.PolicyRecordingIncoming
                            && Interlocked.CompareExchange(ref audioFrameSpeechStartAttempted, 1, 0) == 0)
                        {
                            _logger.LogWarning(
                                "Speech pipeline missing for active audio call. Starting pipeline from audio frame fallback. CallId={CallId}; SessionId={SessionId}; Origin={Origin}; TotalFrames={TotalFrames}; PeakAmplitude={PeakAmplitude}; RmsAmplitude={RmsAmplitude}; SpeechStarted={SpeechStarted}; RecognizerCreated={RecognizerCreated}; PushStreamOpen={PushStreamOpen}",
                                this.callId,
                                this.sessionId,
                                this.origin,
                                receivedFrame.TotalFrames,
                                level.PeakAmplitude,
                                level.RmsAmplitude,
                                preEnqueueSnapshot.Started,
                                preEnqueueSnapshot.RecognizerCreated,
                                preEnqueueSnapshot.PushStreamOpen);

                            _ = StartSpeechTranscriptionCoreAsync(CancellationToken.None)
                                .ForgetAndLogExceptionAsync(this.GraphLogger, "Speech pipeline audio frame fallback startup failed");
                        }

                        if (receivedFrame.ShouldLog)
                        {
                            var snapshot = this.SpeechPipelineSnapshot;
                            _logger.LogInformation(
                                "Audio input level. CallId={CallId}; SessionId={SessionId}; Origin={Origin}; TotalFrames={TotalFrames}; BufferLength={BufferLength}; PeakAmplitude={PeakAmplitude}; RmsAmplitude={RmsAmplitude}; SpeechPipelineReady={SpeechPipelineReady}; SpeechStarted={SpeechStarted}; RecognizerCreated={RecognizerCreated}; PushStreamOpen={PushStreamOpen}; NonZeroAudioDetected={NonZeroAudioDetected}",
                                this.callId,
                                this.sessionId,
                                this.origin,
                                receivedFrame.TotalFrames,
                                buffer.Length,
                                level.PeakAmplitude,
                                level.RmsAmplitude,
                                snapshot.Ready,
                                snapshot.Started,
                                snapshot.RecognizerCreated,
                                snapshot.PushStreamOpen,
                                Volatile.Read(ref nonZeroAudioDetected) == 1);
                        }

                        if (Volatile.Read(ref mixedSpeechFallbackStoppedForUnmixedAudio) == 1)
                        {
                            return;
                        }

                        if (!_languageService.TryEnqueueAudio(buffer, out var dropReason) && receivedFrame.ShouldLog)
                        {
                            var snapshot = this.SpeechPipelineSnapshot;
                            _logger.LogWarning(
                                "Speech audio frame dropped. Reason={Reason}; CallId={CallId}; SessionId={SessionId}; Origin={Origin}; TotalFrames={TotalFrames}; BufferLength={BufferLength}; DroppedFrames={DroppedFrames}; SpeechPipelineReady={SpeechPipelineReady}; SpeechStarted={SpeechStarted}; RecognizerCreated={RecognizerCreated}; PushStreamOpen={PushStreamOpen}; AcceptingFrames={AcceptingFrames}; PeakAmplitude={PeakAmplitude}; RmsAmplitude={RmsAmplitude}",
                                dropReason,
                                this.callId,
                                this.sessionId,
                                this.origin,
                                receivedFrame.TotalFrames,
                                buffer.Length,
                                _languageService.DroppedFrames,
                                snapshot.Ready,
                                snapshot.Started,
                                snapshot.RecognizerCreated,
                                snapshot.PushStreamOpen,
                                snapshot.AcceptingFrames,
                                level.PeakAmplitude,
                                level.RmsAmplitude);
                        }
                    }
                }
                else
                {
                    // send audio buffer back on the audio socket
                    // the particpant talking will hear themselves
                    var length = e.Buffer.Length;
                    if (length > 0)
                    {
                        var buffer = CopyAudioBuffer(e.Buffer);

                        var currentTick = DateTime.Now.Ticks;
                        this.audioMediaBuffers = Util.Utilities.CreateAudioMediaBuffers(buffer, currentTick, _logger);
                        if (this.audioVideoFramePlayer == null)
                        {
                            _logger.LogWarning("Audio video frame player is not available for echo send. CallId={CallId}; TotalFrames={TotalFrames}", this.callId, receivedFrame.TotalFrames);
                            return;
                        }

                        await this.audioVideoFramePlayer.EnqueueBuffersAsync(this.audioMediaBuffers, new List<VideoMediaBuffer>());

                        var sentFrame = this.diagnostics.RecordSentFrame(length, currentTick);
                        if (sentFrame.ShouldLog)
                        {
                            _logger.LogInformation(
                                "Echo audio frame send attempted. CallId={CallId}; TotalFrames={TotalFrames}; BufferLength={BufferLength}; AudioSendStatus={AudioSendStatus}; MediaMode={MediaMode}; Success={Success}",
                                sentFrame.CallId,
                                sentFrame.TotalFrames,
                                sentFrame.BufferLength,
                                this.audioSendStatus,
                                sentFrame.MediaMode,
                                true);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                this.GraphLogger.Error(ex);
                _logger.LogError(
                    ex,
                    "OnAudioMediaReceived error. CallId={CallId}; ExceptionType={ExceptionType}; ReceivedFrames={ReceivedFrames}; SentFrames={SentFrames}",
                    this.callId,
                    ex.GetType().Name,
                    this.diagnostics.ReceivedFrames,
                    this.diagnostics.SentFrames);
            }
            finally
            {
                e.Buffer.Dispose();
            }
        }

        internal static byte[] CopyAudioBuffer(AudioMediaBuffer audioBuffer)
        {
            return CopyPcmFromPointer(audioBuffer.Data, audioBuffer.Length);
        }

        internal static byte[] CopyAudioBuffer(UnmixedAudioBuffer audioBuffer)
        {
            return CopyPcmFromPointer(audioBuffer.Data, audioBuffer.Length);
        }

        private async Task<bool> StartSpeechTranscriptionCoreAsync(CancellationToken cancellationToken)
        {
            await _languageService!.StartAsync(cancellationToken).ConfigureAwait(false);
            return _languageService.Snapshot.Ready;
        }

        private async Task StopAllSpeechTranscriptionAsync(CancellationToken cancellationToken)
        {
            if (_languageService != null)
            {
                await _languageService.StopAsync(cancellationToken).ConfigureAwait(false);
            }

            foreach (var service in speechServicesBySpeakerId.Values)
            {
                await service.StopAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        private async Task<bool> TryProcessUnmixedAudioAsync(AudioMediaBuffer mixedBuffer, long totalFrames)
        {
            var unmixedBuffers = mixedBuffer.UnmixedAudioBuffers;
            if (unmixedBuffers == null || !unmixedBuffers.Any())
            {
                FeedSilenceToInactiveSpeakerServices(activeSpeakerIds: null);
                return false;
            }

            if (!unmixedBuffers.Any(buffer => buffer.Length > 0))
            {
                FeedSilenceToInactiveSpeakerServices(activeSpeakerIds: null);
                return false;
            }

            await StopMixedSpeechFallbackForUnmixedAudioAsync().ConfigureAwait(false);

            var processed = false;
            var activeSpeakerIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var unmixedBuffer in unmixedBuffers)
            {
                if (unmixedBuffer.Length <= 0)
                {
                    continue;
                }

                processed = true;
                var speakerId = unmixedBuffer.ActiveSpeakerId.ToString();
                activeSpeakerIds.Add(speakerId);
                var speaker = ResolveSpeaker(speakerId);
                var service = GetOrCreateSpeakerSpeechService(speakerId, speaker.DisplayName);
                service.SetSessionId(this.sessionId);
                service.SetSpeakerName(speaker.DisplayName);
                await service.StartAsync().ConfigureAwait(false);

                var buffer = CopyAudioBuffer(unmixedBuffer);
                var level = PcmAudioLevelCalculator.Calculate(buffer);
                if (Volatile.Read(ref nonZeroAudioDetected) == 0 && level.PeakAmplitude > 0)
                {
                    Volatile.Write(ref nonZeroAudioDetected, 1);
                    _logger.LogInformation(
                        "First non-zero unmixed audio detected. CallId={CallId}; SessionId={SessionId}; Origin={Origin}; SpeakerId={SpeakerId}; SpeakerName={SpeakerName}; TotalFrames={TotalFrames}; BufferLength={BufferLength}; PeakAmplitude={PeakAmplitude}; RmsAmplitude={RmsAmplitude}",
                        this.callId,
                        this.sessionId,
                        this.origin,
                        speakerId,
                        speaker.DisplayName,
                        totalFrames,
                        buffer.Length,
                        level.PeakAmplitude,
                        level.RmsAmplitude);
                }

                if (!service.TryEnqueueAudio(buffer, out var dropReason))
                {
                    _logger.LogWarning(
                        "Unmixed speech audio frame dropped. Reason={Reason}; CallId={CallId}; SessionId={SessionId}; Origin={Origin}; SpeakerId={SpeakerId}; SpeakerName={SpeakerName}; TotalFrames={TotalFrames}; BufferLength={BufferLength}; DroppedFrames={DroppedFrames}; PeakAmplitude={PeakAmplitude}; RmsAmplitude={RmsAmplitude}",
                        dropReason,
                        this.callId,
                        this.sessionId,
                        this.origin,
                        speakerId,
                        speaker.DisplayName,
                        totalFrames,
                        buffer.Length,
                        service.DroppedFrames,
                        level.PeakAmplitude,
                        level.RmsAmplitude);
                }
            }

            if (processed)
            {
                Volatile.Write(ref unmixedAudioObserved, 1);
            }

            FeedSilenceToInactiveSpeakerServices(activeSpeakerIds);
            return processed;
        }

        private async Task StopMixedSpeechFallbackForUnmixedAudioAsync()
        {
            if (_languageService == null)
            {
                return;
            }

            if (Volatile.Read(ref mixedSpeechFallbackStoppedForUnmixedAudio) == 1)
            {
                return;
            }

            await mixedSpeechFallbackStopLock.WaitAsync().ConfigureAwait(false);
            try
            {
                if (Volatile.Read(ref mixedSpeechFallbackStoppedForUnmixedAudio) == 1)
                {
                    return;
                }

                Volatile.Write(ref unmixedAudioObserved, 1);
                Volatile.Write(ref mixedSpeechFallbackStoppedForUnmixedAudio, 1);
                _logger.LogInformation(
                    "Stopping mixed speech fallback because unmixed speaker audio is available. CallId={CallId}; SessionId={SessionId}; Origin={Origin}",
                    this.callId,
                    this.sessionId,
                    this.origin);
                await _languageService.StopAsync().ConfigureAwait(false);
            }
            finally
            {
                mixedSpeechFallbackStopLock.Release();
            }
        }

        /// <summary>
        /// Feeds one silence frame to every started per-speaker recognizer that had no
        /// unmixed audio in the current 20ms frame. Without this, a silent speaker's
        /// push stream stops advancing: Azure Speech never observes the segmentation
        /// silence timeout (so the in-flight utterance is never finalized) and the
        /// audio offsets freeze, making the next utterance look contiguous with the
        /// previous one.
        /// </summary>
        /// <param name="activeSpeakerIds">Speaker ids that received real audio in this frame, or null when nobody spoke.</param>
        private void FeedSilenceToInactiveSpeakerServices(ISet<string>? activeSpeakerIds)
        {
            foreach (var pair in speechServicesBySpeakerId)
            {
                if (activeSpeakerIds != null && activeSpeakerIds.Contains(pair.Key))
                {
                    continue;
                }

                pair.Value.TryEnqueueAudio(SilencePcmFrame, out _);
            }
        }

        private SpeechService GetOrCreateSpeakerSpeechService(string speakerId, string? speakerName)
        {
            return speechServicesBySpeakerId.GetOrAdd(
                speakerId,
                id => new SpeechService(this.callId, this.appSettings, _logger, transcriptSequenceProvider, transcriptForwarder, statusReporter, this.sessionId, id, speakerName));
        }

        private SpeakerInfo ResolveSpeaker(string speakerId)
        {
            if (speakersBySourceId.TryGetValue(speakerId, out var speaker))
            {
                return speaker;
            }

            return new SpeakerInfo(speakerId, null, null);
        }

        internal static byte[] CopyPcmFromPointer(IntPtr data, long length)
        {
            var buffer = new byte[length];
            Marshal.Copy(data, buffer, 0, (int)length);
            return buffer;
        }

        private static string? NormalizeSpeakerName(string? value)
        {
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }

        private sealed record SpeakerInfo(string SourceId, string? DisplayName, string? ParticipantId);
    }
}

