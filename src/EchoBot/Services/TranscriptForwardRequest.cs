using System.Text.Json.Serialization;

namespace EchoBot.Services
{
    public sealed class TranscriptForwardRequest
    {
        public TranscriptForwardRequest(
            string eventId,
            string callId,
            int sequenceNo,
            DateTimeOffset recognizedAtUtc,
            long? offsetTicks,
            long? durationTicks,
            string text)
        {
            EventId = eventId;
            CallId = callId;
            SequenceNo = sequenceNo;
            RecognizedAtUtc = recognizedAtUtc;
            OffsetTicks = offsetTicks;
            DurationTicks = durationTicks;
            Text = text;
        }

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
