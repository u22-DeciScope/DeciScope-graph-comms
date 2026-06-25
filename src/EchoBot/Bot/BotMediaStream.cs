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
        private readonly string callId;
        private readonly MediaDiagnostics diagnostics;
        private MediaSendStatus audioSendStatus = MediaSendStatus.Inactive;

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
            ITranscriptRepository transcriptRepository,
            ITranscriptForwarder transcriptForwarder
        )
            : base(graphLogger)
        {
            ArgumentVerifier.ThrowOnNullArgument(mediaSession, nameof(mediaSession));
            ArgumentVerifier.ThrowOnNullArgument(logger, nameof(logger));
            ArgumentVerifier.ThrowOnNullArgument(settings, nameof(settings));

            _settings = settings;
            _logger = logger;
            this.callId = callId;
            this.diagnostics = new MediaDiagnostics(callId, _settings.UseSpeechService);

            _logger.LogInformation(
                "Bot media mode: {MediaMode}. CallId={CallId}; UseSpeechService={UseSpeechService}",
                this.diagnostics.ModeName,
                this.callId,
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
                _languageService = new SpeechService(this.callId, _settings, _logger, transcriptRepository, transcriptForwarder);
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
                "BotMediaStream initialized. CallId={CallId}; HasAudioSocket={HasAudioSocket}; MediaMode={MediaMode}",
                this.callId,
                this._audioSocket != null,
                this.diagnostics.ModeName);
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

        public Task StartSpeechTranscriptionAsync(CancellationToken cancellationToken = default)
        {
            if (_languageService == null)
            {
                return Task.CompletedTask;
            }

            return _languageService.StartAsync(cancellationToken);
        }

        public Task StopSpeechTranscriptionAsync(CancellationToken cancellationToken = default)
        {
            if (_languageService == null)
            {
                return Task.CompletedTask;
            }

            return _languageService.StopAsync(cancellationToken);
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
                    var length = e.Buffer.Length;
                    if (length > 0)
                    {
                        var buffer = CopyAudioBuffer(e.Buffer);
                        var level = PcmAudioLevelCalculator.Calculate(buffer);
                        if (receivedFrame.ShouldLog)
                        {
                            _logger.LogInformation(
                                "Audio input level. CallId={CallId}; TotalFrames={TotalFrames}; BufferLength={BufferLength}; PeakAmplitude={PeakAmplitude}; RmsAmplitude={RmsAmplitude}",
                                this.callId,
                                receivedFrame.TotalFrames,
                                buffer.Length,
                                level.PeakAmplitude,
                                level.RmsAmplitude);
                        }

                        if (!_languageService.TryEnqueueAudio(buffer) && receivedFrame.ShouldLog)
                        {
                            _logger.LogWarning(
                                "Speech audio frame was not accepted. CallId={CallId}; TotalFrames={TotalFrames}; DroppedFrames={DroppedFrames}",
                                this.callId,
                                receivedFrame.TotalFrames,
                                _languageService.DroppedFrames);
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

        internal static byte[] CopyPcmFromPointer(IntPtr data, long length)
        {
            var buffer = new byte[length];
            Marshal.Copy(data, buffer, 0, (int)length);
            return buffer;
        }
    }
}

