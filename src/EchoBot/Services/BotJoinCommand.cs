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
    }
}
