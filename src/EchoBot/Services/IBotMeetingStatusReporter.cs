using EchoBot.Bot;

namespace EchoBot.Services
{
    public interface IBotMeetingStatusReporter
    {
        Task ReportAsync(
            string? sessionId,
            string status,
            string message,
            string? botCallId = null,
            CancellationToken cancellationToken = default,
            string? failedReason = null,
            string? errorCode = null,
            string? source = null,
            string? endReason = null,
            DateTimeOffset? endedAt = null,
            long? lastFinalSequenceNo = null,
            bool? transcriptQueueDrained = null);

        Task ReportMetadataAsync(
            string? sessionId,
            BotMeetingMetadataUpdate metadata,
            CancellationToken cancellationToken = default);

        Task ReportHeartbeatAsync(
            string? sessionId,
            string? botCallId,
            CancellationToken cancellationToken = default,
            BotMediaMetricsSnapshot? metrics = null);

        TimeSpan? HeartbeatInterval { get; }
    }
}
