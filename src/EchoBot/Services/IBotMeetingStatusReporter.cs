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
            DateTimeOffset? endedAt = null);
    }
}
