using System.Text.Json.Serialization;

namespace EchoBot.Services
{
    public sealed class BotEndCommand
    {
        [JsonPropertyName("sessionId")]
        public string? SessionId { get; set; }

        [JsonPropertyName("botCallId")]
        public string? BotCallId { get; set; }

        [JsonPropertyName("reason")]
        public string? Reason { get; set; }
    }
}
