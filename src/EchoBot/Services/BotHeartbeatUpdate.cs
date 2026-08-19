using System.Text.Json.Serialization;
using EchoBot.Bot;

namespace EchoBot.Services
{
    public sealed class BotHeartbeatUpdate
    {
        public BotHeartbeatUpdate(string? botCallId, BotMediaMetricsSnapshot? metrics = null)
        {
            BotCallId = botCallId;
            var build = BuildFingerprint.Current;
            BotBuildVersion = build.BuildVersion;
            BotGitCommitSha = build.GitCommitSha;
            BotBuildTimestamp = build.BuildTimestamp;
            BotDirtyBuild = build.DirtyBuild;

            LastAudioFrameAtUtc = FormatUtc(metrics?.LastAudioFrameAtUtc);
            LastNonZeroAudioAtUtc = FormatUtc(metrics?.LastNonZeroAudioAtUtc);
            LastNonEmptyTranscriptAtUtc = FormatUtc(metrics?.LastNonEmptyTranscriptAtUtc);
            LastFinalTranscriptAtUtc = FormatUtc(metrics?.LastFinalTranscriptAtUtc);
            LastPeakAmplitude = metrics?.LastPeakAmplitude;
            LastRmsAmplitude = metrics?.LastRmsAmplitude;
            AudioFrameCount = metrics?.AudioFrameCount;
            FramesSinceLastNonZeroAudio = metrics?.FramesSinceLastNonZeroAudio;
            SecondsSinceLastNonZeroAudio = metrics?.SecondsSinceLastNonZeroAudio;
            ActiveSpeakerRecognizerCount = metrics?.ActiveSpeakerRecognizerCount;
            MixedFallbackActive = metrics?.MixedFallbackActive;
            UnmixedAudioSeen = metrics?.UnmixedAudioSeen;
            LastAudioSocketReceiveStallAtUtc = FormatUtc(metrics?.LastAudioSocketReceiveStallAtUtc);
            AudioSocketReceiveStallCount = metrics?.AudioSocketReceiveStallCount;
            AudioStalled = metrics?.AudioStalled;
            SpeechPipelineReady = metrics?.SpeechPipelineReady;
            SpeechStarted = metrics?.SpeechStarted;
            SpeechAcceptingFrames = metrics?.SpeechAcceptingFrames;
            RecognizerCreated = metrics?.RecognizerCreated;
            PushStreamOpen = metrics?.PushStreamOpen;
            PipelineGeneration = metrics?.PipelineGeneration;
            RecognizerInstanceIdHash = metrics?.RecognizerInstanceIdHash;
            LastRecognizerStartedAtUtc = FormatUtc(metrics?.LastRecognizerStartedAtUtc);
            LastSpeechPartialAtUtc = FormatUtc(metrics?.LastSpeechPartialAtUtc);
            LastSpeechFinalAtUtc = FormatUtc(metrics?.LastSpeechFinalAtUtc);
        }

        [JsonPropertyName("botCallId")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? BotCallId { get; }

        [JsonPropertyName("botBuildVersion")]
        public string BotBuildVersion { get; }

        [JsonPropertyName("botGitCommitSha")]
        public string BotGitCommitSha { get; }

        [JsonPropertyName("botBuildTimestamp")]
        public string BotBuildTimestamp { get; }

        [JsonPropertyName("botDirtyBuild")]
        public string BotDirtyBuild { get; }

        [JsonPropertyName("lastAudioFrameAtUtc")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? LastAudioFrameAtUtc { get; }

        [JsonPropertyName("lastNonZeroAudioAtUtc")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? LastNonZeroAudioAtUtc { get; }

        [JsonPropertyName("lastNonEmptyTranscriptAtUtc")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? LastNonEmptyTranscriptAtUtc { get; }

        [JsonPropertyName("lastFinalTranscriptAtUtc")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? LastFinalTranscriptAtUtc { get; }

        [JsonPropertyName("lastPeakAmplitude")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public int? LastPeakAmplitude { get; }

        [JsonPropertyName("lastRmsAmplitude")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public double? LastRmsAmplitude { get; }

        [JsonPropertyName("audioFrameCount")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public long? AudioFrameCount { get; }

        [JsonPropertyName("framesSinceLastNonZeroAudio")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public long? FramesSinceLastNonZeroAudio { get; }

        [JsonPropertyName("secondsSinceLastNonZeroAudio")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public int? SecondsSinceLastNonZeroAudio { get; }

        [JsonPropertyName("activeSpeakerRecognizerCount")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public int? ActiveSpeakerRecognizerCount { get; }

        [JsonPropertyName("mixedFallbackActive")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public bool? MixedFallbackActive { get; }

        [JsonPropertyName("unmixedAudioSeen")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public bool? UnmixedAudioSeen { get; }

        [JsonPropertyName("lastAudioSocketReceiveStallAtUtc")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? LastAudioSocketReceiveStallAtUtc { get; }

        [JsonPropertyName("audioSocketReceiveStallCount")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public long? AudioSocketReceiveStallCount { get; }

        [JsonPropertyName("audioStalled")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public bool? AudioStalled { get; }

        [JsonPropertyName("speechPipelineReady")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public bool? SpeechPipelineReady { get; }

        [JsonPropertyName("speechStarted")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public bool? SpeechStarted { get; }

        [JsonPropertyName("speechAcceptingFrames")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public bool? SpeechAcceptingFrames { get; }

        [JsonPropertyName("recognizerCreated")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public bool? RecognizerCreated { get; }

        [JsonPropertyName("pushStreamOpen")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public bool? PushStreamOpen { get; }

        [JsonPropertyName("pipelineGeneration")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public long? PipelineGeneration { get; }

        [JsonPropertyName("recognizerInstanceIdHash")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? RecognizerInstanceIdHash { get; }

        [JsonPropertyName("lastRecognizerStartedAtUtc")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? LastRecognizerStartedAtUtc { get; }

        [JsonPropertyName("lastSpeechPartialAtUtc")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? LastSpeechPartialAtUtc { get; }

        [JsonPropertyName("lastSpeechFinalAtUtc")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? LastSpeechFinalAtUtc { get; }

        private static string? FormatUtc(DateTimeOffset? value)
        {
            return value?.ToString("o");
        }
    }
}
