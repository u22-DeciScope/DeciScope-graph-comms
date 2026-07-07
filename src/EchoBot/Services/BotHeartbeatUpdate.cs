using System.Text.Json.Serialization;

namespace EchoBot.Services
{
    public sealed class BotHeartbeatUpdate
    {
        public BotHeartbeatUpdate(string? botCallId)
        {
            BotCallId = botCallId;
        }

        [JsonPropertyName("botCallId")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? BotCallId { get; }
    }
}
