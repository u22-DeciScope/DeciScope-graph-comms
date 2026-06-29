namespace EchoBot.Models
{
    public sealed class TranscriptSegment
    {
        public string? SessionId { get; init; }

        public string CallId { get; init; } = string.Empty;

        public string? SpeakerId { get; init; }

        public string? SpeakerName { get; init; }

        public string RecognizedAtUtc { get; init; } = string.Empty;

        public long? OffsetTicks { get; init; }

        public long? DurationTicks { get; init; }

        public string Text { get; init; } = string.Empty;

        public bool IsFinal { get; init; } = true;
    }
}
