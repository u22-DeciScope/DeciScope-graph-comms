namespace EchoBot.Services
{
    public interface IBotMeetingStatusReporter
    {
        Task ReportAsync(
            string? sessionId,
            string status,
            string message,
            string? botCallId = null,
            CancellationToken cancellationToken = default);
    }
}
