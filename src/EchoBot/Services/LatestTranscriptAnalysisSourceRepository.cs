using System.Text;
using Microsoft.Data.Sqlite;

namespace EchoBot.Services
{
    public sealed class LatestTranscriptAnalysisSourceRepository
    {
        private const int MaxSegments = 30;

        private readonly ITranscriptRepository transcriptRepository;
        private readonly ILogger<LatestTranscriptAnalysisSourceRepository> logger;

        public LatestTranscriptAnalysisSourceRepository(
            ITranscriptRepository transcriptRepository,
            ILogger<LatestTranscriptAnalysisSourceRepository> logger)
        {
            this.transcriptRepository = transcriptRepository;
            this.logger = logger;
        }

        public async Task<LatestTranscriptAnalysisSource?> GetLatestAsync(CancellationToken cancellationToken = default)
        {
            await transcriptRepository.InitializeAsync(cancellationToken).ConfigureAwait(false);

            await using var connection = CreateConnection();
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            var latestMeeting = await GetLatestMeetingAsync(connection, cancellationToken).ConfigureAwait(false);
            if (latestMeeting == null)
            {
                logger.LogInformation("No transcript segments found for AI analysis. DatabasePath={DatabasePath}", transcriptRepository.DatabasePath);
                return null;
            }

            var segments = await GetLatestSegmentsAsync(connection, latestMeeting, cancellationToken).ConfigureAwait(false);
            if (segments.Count == 0)
            {
                logger.LogInformation(
                    "No transcript segments found for latest meeting. MeetingId={MeetingId}; MeetingIdSource={MeetingIdSource}",
                    latestMeeting.MeetingId,
                    latestMeeting.MeetingIdSource);
                return null;
            }

            var inputText = FormatInput(latestMeeting.MeetingId, segments);
            return new LatestTranscriptAnalysisSource(latestMeeting.MeetingId, latestMeeting.MeetingIdSource, segments.Count, inputText);
        }

        private async Task<LatestMeeting?> GetLatestMeetingAsync(SqliteConnection connection, CancellationToken cancellationToken)
        {
            await using var command = connection.CreateCommand();
            command.CommandText =
                @"SELECT
                    CASE
                        WHEN session_id IS NOT NULL AND trim(session_id) <> '' THEN session_id
                        ELSE call_id
                    END AS meeting_id,
                    CASE
                        WHEN session_id IS NOT NULL AND trim(session_id) <> '' THEN 'session_id'
                        ELSE 'call_id'
                    END AS meeting_id_source
                FROM transcript_segments
                WHERE text IS NOT NULL AND trim(text) <> ''
                ORDER BY datetime(created_at_utc) DESC, id DESC
                LIMIT 1;";

            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                return null;
            }

            var meetingId = reader.IsDBNull(0) ? null : reader.GetString(0);
            if (string.IsNullOrWhiteSpace(meetingId))
            {
                return null;
            }

            return new LatestMeeting(meetingId, reader.GetString(1));
        }

        private static async Task<IReadOnlyList<TranscriptAnalysisSegment>> GetLatestSegmentsAsync(
            SqliteConnection connection,
            LatestMeeting latestMeeting,
            CancellationToken cancellationToken)
        {
            await using var command = connection.CreateCommand();
            command.CommandText =
                @"SELECT sequence_no, speaker_name, speaker_id, text
                FROM (
                    SELECT id, sequence_no, speaker_name, speaker_id, text
                    FROM transcript_segments
                    WHERE
                        text IS NOT NULL
                        AND trim(text) <> ''
                        AND CASE
                            WHEN session_id IS NOT NULL AND trim(session_id) <> '' THEN session_id
                            ELSE call_id
                        END = $meeting_id
                    ORDER BY sequence_no DESC, id DESC
                    LIMIT $limit
                )
                ORDER BY sequence_no ASC, id ASC;";
            command.Parameters.AddWithValue("$meeting_id", latestMeeting.MeetingId);
            command.Parameters.AddWithValue("$limit", MaxSegments);

            var segments = new List<TranscriptAnalysisSegment>();
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var speakerName = reader.IsDBNull(1) ? null : reader.GetString(1);
                var speakerId = reader.IsDBNull(2) ? null : reader.GetString(2);
                segments.Add(new TranscriptAnalysisSegment(
                    reader.GetInt32(0),
                    FirstNonEmpty(speakerName, speakerId, "unknown"),
                    reader.GetString(3)));
            }

            return segments;
        }

        private static string FormatInput(string meetingId, IReadOnlyList<TranscriptAnalysisSegment> segments)
        {
            var builder = new StringBuilder();
            builder.Append("会議ID: ").AppendLine(meetingId);
            builder.AppendLine();
            builder.AppendLine("発話:");
            foreach (var segment in segments)
            {
                builder
                    .Append('[')
                    .Append(segment.SequenceNo)
                    .Append("] ")
                    .Append(segment.SpeakerName)
                    .Append(": ")
                    .AppendLine(segment.Text);
            }

            return builder.ToString().TrimEnd();
        }

        private SqliteConnection CreateConnection()
        {
            var builder = new SqliteConnectionStringBuilder
            {
                DataSource = transcriptRepository.DatabasePath,
            };

            return new SqliteConnection(builder.ToString());
        }

        private static string FirstNonEmpty(params string?[] values)
        {
            foreach (var value in values)
            {
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value;
                }
            }

            return string.Empty;
        }

        private sealed class LatestMeeting
        {
            public LatestMeeting(string meetingId, string meetingIdSource)
            {
                MeetingId = meetingId;
                MeetingIdSource = meetingIdSource;
            }

            public string MeetingId { get; }

            public string MeetingIdSource { get; }
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

    public sealed class TranscriptAnalysisSegment
    {
        public TranscriptAnalysisSegment(int sequenceNo, string speakerName, string text)
        {
            SequenceNo = sequenceNo;
            SpeakerName = speakerName;
            Text = text;
        }

        public int SequenceNo { get; }

        public string SpeakerName { get; }

        public string Text { get; }
    }
}
