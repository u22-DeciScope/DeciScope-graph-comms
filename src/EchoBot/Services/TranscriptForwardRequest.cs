using System.Text.Json.Serialization;

namespace EchoBot.Services
{
    public sealed class TranscriptForwardRequest
    {
        public TranscriptForwardRequest(
            string? sessionId,
            string eventId,
            string callId,
            int sequenceNo,
            DateTimeOffset recognizedAtUtc,
            long? offsetTicks,
            long? durationTicks,
            string text)
        {
            SessionId = sessionId;
            EventId = eventId;
            CallId = callId;
            SequenceNo = sequenceNo;
            RecognizedAtUtc = recognizedAtUtc;
            OffsetTicks = offsetTicks;
            DurationTicks = durationTicks;
            Text = text;
        }

        [JsonPropertyName("sessionId")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? SessionId { get; }

        [JsonPropertyName("eventId")]
        public string EventId { get; }

        [JsonPropertyName("callId")]
        public string CallId { get; }

        [JsonPropertyName("sequenceNo")]
        public int SequenceNo { get; }

        [JsonPropertyName("recognizedAtUtc")]
        public DateTimeOffset RecognizedAtUtc { get; }

        [JsonPropertyName("offsetTicks")]
        public long? OffsetTicks { get; }

        [JsonPropertyName("durationTicks")]
        public long? DurationTicks { get; }

        [JsonPropertyName("text")]
        public string Text { get; }
    }
}
