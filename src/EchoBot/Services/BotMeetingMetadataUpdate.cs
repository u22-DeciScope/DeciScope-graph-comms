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
            string? joinMeetingId,
            string? joinWebUrl,
            string? canonicalJoinWebUrl,
            string? threadId,
            string? organizerId,
            string? organizerName = null,
            string? organizerEmail = null,
            DateTimeOffset? scheduledStartAt = null,
            DateTimeOffset? scheduledEndAt = null,
            string? titleResolutionErrorCode = null,
            string? titleResolutionErrorMessage = null,
            DateTimeOffset? titleResolvedAt = null)
        {
            Title = title;
            TitleSource = titleSource;
            Provider = provider;
            ExternalMeetingId = externalMeetingId;
            JoinMeetingId = joinMeetingId;
            JoinWebUrl = joinWebUrl;
            CanonicalJoinWebUrl = canonicalJoinWebUrl;
            ThreadId = threadId;
            OrganizerId = organizerId;
            OrganizerName = organizerName;
            OrganizerEmail = organizerEmail;
            ScheduledStartAt = scheduledStartAt;
            ScheduledEndAt = scheduledEndAt;
            TitleResolutionErrorCode = titleResolutionErrorCode;
            TitleResolutionErrorMessage = titleResolutionErrorMessage;
            TitleResolvedAt = titleResolvedAt;
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

        [JsonPropertyName("joinMeetingId")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? JoinMeetingId { get; }

        [JsonPropertyName("joinWebUrl")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? JoinWebUrl { get; }

        [JsonPropertyName("canonicalJoinWebUrl")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? CanonicalJoinWebUrl { get; }

        [JsonPropertyName("threadId")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? ThreadId { get; }

        [JsonPropertyName("organizerId")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? OrganizerId { get; }

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

        [JsonPropertyName("titleResolutionErrorCode")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? TitleResolutionErrorCode { get; }

        [JsonPropertyName("titleResolutionErrorMessage")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? TitleResolutionErrorMessage { get; }

        [JsonPropertyName("titleResolvedAt")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public DateTimeOffset? TitleResolvedAt { get; }
    }
}
