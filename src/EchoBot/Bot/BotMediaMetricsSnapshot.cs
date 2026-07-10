namespace EchoBot.Bot
{
    /// <summary>
    /// Point-in-time snapshot of a <see cref="BotMediaStream"/>'s audio/transcription metrics, taken for the
    /// DeciScope heartbeat report so the API can classify a session as silent / audio_stalled /
    /// speech_stalled. See <see cref="BotMediaStream.GetMediaMetricsSnapshot"/>.
    /// </summary>
    public sealed record BotMediaMetricsSnapshot
    {
        public DateTimeOffset? LastAudioFrameAtUtc { get; init; }

        public DateTimeOffset? LastNonZeroAudioAtUtc { get; init; }

        public int LastPeakAmplitude { get; init; }

        public double LastRmsAmplitude { get; init; }

        public long AudioFrameCount { get; init; }

        public long FramesSinceLastNonZeroAudio { get; init; }

        public int SecondsSinceLastNonZeroAudio { get; init; }

        public int ActiveSpeakerRecognizerCount { get; init; }

        public bool MixedFallbackActive { get; init; }

        public bool UnmixedAudioSeen { get; init; }

        public DateTimeOffset? LastNonEmptyTranscriptAtUtc { get; init; }

        public DateTimeOffset? LastFinalTranscriptAtUtc { get; init; }

        public DateTimeOffset? LastAudioSocketReceiveStallAtUtc { get; init; }

        public long AudioSocketReceiveStallCount { get; init; }

        public bool AudioStalled { get; init; }
    }

    /// <summary>
    /// Small pure helpers used to compute a few of <see cref="BotMediaMetricsSnapshot"/>'s derived fields,
    /// split out of <see cref="BotMediaStream"/> so they can be unit tested directly.
    /// </summary>
    internal static class BotMediaMetricsCalculator
    {
        /// <summary>
        /// How recent the last detected audio-socket receive stall must be for
        /// <see cref="BotMediaMetricsSnapshot.AudioStalled"/> to be reported as true.
        /// </summary>
        public static readonly TimeSpan AudioStalledRecentWindow = TimeSpan.FromSeconds(30);

        public static long FramesSinceLastNonZeroAudio(long receivedFrames, long framesAtLastNonZeroAudio, bool hasObservedNonZeroAudio)
        {
            if (!hasObservedNonZeroAudio)
            {
                return receivedFrames;
            }

            var delta = receivedFrames - framesAtLastNonZeroAudio;
            return delta < 0 ? 0 : delta;
        }

        public static int SecondsSinceLastNonZeroAudio(DateTimeOffset now, DateTimeOffset? lastNonZeroAudioAtUtc)
        {
            if (lastNonZeroAudioAtUtc == null)
            {
                return 0;
            }

            var elapsedSeconds = (now - lastNonZeroAudioAtUtc.Value).TotalSeconds;
            if (elapsedSeconds <= 0)
            {
                return 0;
            }

            return elapsedSeconds >= int.MaxValue ? int.MaxValue : (int)elapsedSeconds;
        }

        public static bool IsAudioStalled(DateTimeOffset now, DateTimeOffset? lastStallAtUtc, TimeSpan recentWindow)
        {
            if (lastStallAtUtc == null)
            {
                return false;
            }

            var elapsed = now - lastStallAtUtc.Value;
            return elapsed >= TimeSpan.Zero && elapsed <= recentWindow;
        }

        public static DateTimeOffset? Latest(DateTimeOffset? a, DateTimeOffset? b)
        {
            if (a == null)
            {
                return b;
            }

            if (b == null)
            {
                return a;
            }

            return a.Value >= b.Value ? a : b;
        }
    }
}
