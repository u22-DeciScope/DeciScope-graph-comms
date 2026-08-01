using System.Text.Json.Serialization;

namespace EchoBot.Services
{
    public sealed class BotMediaHealthUpdate
    {
        public BotMediaHealthUpdate(
            string eventId,
            string state,
            string eventName,
            DateTimeOffset occurredAtUtc,
            DateTimeOffset startedAtUtc,
            DateTimeOffset lastAudioFrameAtUtc,
            long durationMs,
            string source = "audio_frame_watchdog")
        {
            EventId = eventId;
            State = state;
            Event = eventName;
            OccurredAtUtc = occurredAtUtc.ToString("o");
            StartedAtUtc = startedAtUtc.ToString("o");
            LastAudioFrameAtUtc = lastAudioFrameAtUtc.ToString("o");
            DurationMs = durationMs;
            Source = source;
        }

        [JsonPropertyName("eventId")]
        public string EventId { get; }

        [JsonPropertyName("state")]
        public string State { get; }

        [JsonPropertyName("event")]
        public string Event { get; }

        [JsonPropertyName("occurredAtUtc")]
        public string OccurredAtUtc { get; }

        [JsonPropertyName("startedAtUtc")]
        public string StartedAtUtc { get; }

        [JsonPropertyName("lastAudioFrameAtUtc")]
        public string LastAudioFrameAtUtc { get; }

        [JsonPropertyName("durationMs")]
        public long DurationMs { get; }

        [JsonPropertyName("source")]
        public string Source { get; }

        [JsonPropertyName("botCallId")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? BotCallId { get; init; }

        public BotMediaHealthUpdate WithBotCallId(string? botCallId)
        {
            return new BotMediaHealthUpdate(
                EventId,
                State,
                Event,
                DateTimeOffset.Parse(OccurredAtUtc),
                DateTimeOffset.Parse(StartedAtUtc),
                DateTimeOffset.Parse(LastAudioFrameAtUtc),
                DurationMs,
                Source)
            {
                BotCallId = botCallId,
            };
        }
    }
}
