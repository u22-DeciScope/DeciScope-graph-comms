using System.Text.Json.Serialization;

namespace EchoBot.Services
{
    public sealed class BotJoinCommand
    {
        [JsonPropertyName("sessionId")]
        public string? SessionId { get; set; }

        [JsonPropertyName("joinUrl")]
        public string? JoinUrl { get; set; }

        [JsonPropertyName("tenantId")]
        public string? TenantId { get; set; }

        [JsonPropertyName("candidateUserIds")]
        public IReadOnlyCollection<string>? CandidateUserIds { get; set; }

        [JsonPropertyName("createdByMicrosoftUserId")]
        public string? CreatedByMicrosoftUserId { get; set; }

        [JsonPropertyName("createdByEmail")]
        public string? CreatedByEmail { get; set; }

        [JsonPropertyName("joinMeetingId")]
        public string? JoinMeetingId { get; set; }

        [JsonPropertyName("canonicalJoinWebUrl")]
        public string? CanonicalJoinWebUrl { get; set; }
    }
}
