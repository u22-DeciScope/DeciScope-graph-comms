using System.Text.Json.Serialization;

namespace EchoBot.Services
{
    public sealed class BotMeetingStatusUpdate
    {
        public BotMeetingStatusUpdate(
            string status,
            string? botCallId,
            string message,
            string? failedReason = null,
            string? errorCode = null,
            string? source = null,
            string? endReason = null,
            DateTimeOffset? endedAt = null)
        {
            Status = status;
            BotCallId = botCallId;
            Message = message;
            FailedReason = failedReason;
            ErrorCode = errorCode;
            Source = source;
            EndReason = endReason;
            EndedAt = endedAt;
        }

        [JsonPropertyName("status")]
        public string Status { get; }

        [JsonPropertyName("botCallId")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? BotCallId { get; }

        [JsonPropertyName("message")]
        public string Message { get; }

        [JsonPropertyName("failedReason")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? FailedReason { get; }

        [JsonPropertyName("errorCode")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? ErrorCode { get; }

        [JsonPropertyName("source")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Source { get; }

        [JsonPropertyName("endReason")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? EndReason { get; }

        [JsonPropertyName("endedAt")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public DateTimeOffset? EndedAt { get; }
    }
}
