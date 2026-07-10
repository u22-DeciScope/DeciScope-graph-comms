using System.Text.Json.Serialization;
using EchoBot.Bot;

namespace EchoBot.Services
{
    public sealed class BotHeartbeatUpdate
    {
        public BotHeartbeatUpdate(string? botCallId, BotMediaMetricsSnapshot? metrics = null)
        {
            BotCallId = botCallId;

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
        }

        [JsonPropertyName("botCallId")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? BotCallId { get; }

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

        private static string? FormatUtc(DateTimeOffset? value)
        {
            return value?.ToString("o");
        }
    }
}
