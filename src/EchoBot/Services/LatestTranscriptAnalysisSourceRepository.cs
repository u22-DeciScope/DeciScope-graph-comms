namespace EchoBot.Services
{
    public sealed class LatestTranscriptAnalysisSourceRepository
    {
        private readonly ILogger<LatestTranscriptAnalysisSourceRepository> logger;

        public LatestTranscriptAnalysisSourceRepository(
            ILogger<LatestTranscriptAnalysisSourceRepository> logger)
        {
            this.logger = logger;
        }

        public Task<LatestTranscriptAnalysisSource?> GetLatestAsync(CancellationToken cancellationToken = default)
        {
            logger.LogInformation("Latest transcript AI analysis source is unavailable because local SQLite transcript storage is disabled.");
            return Task.FromResult<LatestTranscriptAnalysisSource?>(null);
        }
    }

    public sealed class LatestTranscriptAnalysisSource
    {
        public LatestTranscriptAnalysisSource(string meetingId, string meetingIdSource, int segmentCount, string inputText)
        {
            MeetingId = meetingId;
            MeetingIdSource = meetingIdSource;
            SegmentCount = segmentCount;
            InputText = inputText;
        }

        public string MeetingId { get; }

        public string MeetingIdSource { get; }

        public int SegmentCount { get; }

        public string InputText { get; }
    }
}
