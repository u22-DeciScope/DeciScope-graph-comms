using System.Text.Json.Serialization;

namespace EchoBot.Services
{
    public sealed class BotMeetingStatusUpdate
    {
        public BotMeetingStatusUpdate(string status, string? botCallId, string message)
        {
            Status = status;
            BotCallId = botCallId;
            Message = message;
        }

        [JsonPropertyName("status")]
        public string Status { get; }

        [JsonPropertyName("botCallId")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? BotCallId { get; }

        [JsonPropertyName("message")]
        public string Message { get; }
    }
}
