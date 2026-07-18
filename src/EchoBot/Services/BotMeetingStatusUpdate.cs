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
            DateTimeOffset? endedAt = null,
            long? lastFinalSequenceNo = null,
            bool? transcriptQueueDrained = null)
        {
            Status = status;
            BotCallId = botCallId;
            Message = message;
            FailedReason = failedReason;
            ErrorCode = errorCode;
            Source = source;
            EndReason = endReason;
            EndedAt = endedAt;
            LastFinalSequenceNo = lastFinalSequenceNo;
            TranscriptQueueDrained = transcriptQueueDrained;
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

        /// <summary>
        /// APIへの転送成功が確認できたfinal transcriptの最大sequence番号。
        /// 1件も転送成功していない場合はnull(JSONから省略され、APIはDB静穏判定へ
        /// fallbackする)。
        /// </summary>
        [JsonPropertyName("lastFinalSequenceNo")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public long? LastFinalSequenceNo { get; }

        /// <summary>
        /// セッションの転送queueがdrain完了したか。timeout等で完了を確認できなかった
        /// 場合はfalseを送る(trueと偽らない)。旧挙動(未通知)はnull。
        /// </summary>
        [JsonPropertyName("transcriptQueueDrained")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public bool? TranscriptQueueDrained { get; }
    }
}
