using System.Text.Json.Serialization;

namespace EchoBot.Services
{
    public sealed class BotMeetingMetadataUpdate
    {
        public BotMeetingMetadataUpdate(
            string? title,
            string? titleSource,
            string? provider,
            string? externalMeetingId,
            string? threadId,
            string? organizerName = null,
            string? organizerEmail = null,
            DateTimeOffset? scheduledStartAt = null,
            DateTimeOffset? scheduledEndAt = null)
        {
            Title = title;
            TitleSource = titleSource;
            Provider = provider;
            ExternalMeetingId = externalMeetingId;
            ThreadId = threadId;
            OrganizerName = organizerName;
            OrganizerEmail = organizerEmail;
            ScheduledStartAt = scheduledStartAt;
            ScheduledEndAt = scheduledEndAt;
        }

        [JsonPropertyName("title")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Title { get; }

        [JsonPropertyName("titleSource")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? TitleSource { get; }

        [JsonPropertyName("provider")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Provider { get; }

        [JsonPropertyName("externalMeetingId")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? ExternalMeetingId { get; }

        [JsonPropertyName("threadId")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? ThreadId { get; }

        [JsonPropertyName("organizerName")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? OrganizerName { get; }

        [JsonPropertyName("organizerEmail")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? OrganizerEmail { get; }

        [JsonPropertyName("scheduledStartAt")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public DateTimeOffset? ScheduledStartAt { get; }

        [JsonPropertyName("scheduledEndAt")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public DateTimeOffset? ScheduledEndAt { get; }
    }
}
