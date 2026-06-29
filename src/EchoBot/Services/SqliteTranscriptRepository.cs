using EchoBot.Models;
using Microsoft.Data.Sqlite;

namespace EchoBot.Services
{
    public sealed class SqliteTranscriptRepository : ITranscriptRepository
    {
        private const string DatabasePathEnvironmentVariable = "DECISCOPE_SQLITE_PATH";

        private readonly SemaphoreSlim writeLock = new SemaphoreSlim(1, 1);
        private readonly ILogger<SqliteTranscriptRepository> logger;
        private bool initialized;

        public SqliteTranscriptRepository(ILogger<SqliteTranscriptRepository> logger)
        {
            this.logger = logger;
            DatabasePath = ResolveDatabasePath();
        }

        public string DatabasePath { get; }

        public async Task InitializeAsync(CancellationToken cancellationToken = default)
        {
            if (initialized)
            {
                return;
            }

            await writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (initialized)
                {
                    return;
                }

                var directory = Path.GetDirectoryName(DatabasePath);
                if (!string.IsNullOrWhiteSpace(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                await using var connection = CreateConnection();
                await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

                await ExecuteNonQueryAsync(connection, "PRAGMA journal_mode=WAL;", cancellationToken).ConfigureAwait(false);
                await ExecuteNonQueryAsync(connection, "PRAGMA busy_timeout=5000;", cancellationToken).ConfigureAwait(false);
                await ExecuteNonQueryAsync(
                    connection,
                    @"CREATE TABLE IF NOT EXISTS transcript_segments (
                        id                INTEGER PRIMARY KEY AUTOINCREMENT,
                        session_id        TEXT,
                        call_id           TEXT    NOT NULL,
                        speaker_id        TEXT,
                        speaker_name      TEXT,
                        sequence_no       INTEGER NOT NULL,
                        recognized_at_utc TEXT    NOT NULL,
                        offset_ticks      INTEGER,
                        duration_ticks    INTEGER,
                        text              TEXT    NOT NULL,
                        created_at_utc    TEXT    NOT NULL,
                        UNIQUE (call_id, sequence_no)
                    );",
                    cancellationToken).ConfigureAwait(false);
                await EnsureColumnAsync(connection, "session_id", "TEXT", cancellationToken).ConfigureAwait(false);
                await EnsureColumnAsync(connection, "speaker_id", "TEXT", cancellationToken).ConfigureAwait(false);
                await EnsureColumnAsync(connection, "speaker_name", "TEXT", cancellationToken).ConfigureAwait(false);
                await ExecuteNonQueryAsync(
                    connection,
                    @"CREATE INDEX IF NOT EXISTS idx_transcript_call_order
                        ON transcript_segments (call_id, sequence_no);",
                    cancellationToken).ConfigureAwait(false);

                initialized = true;
                logger.LogInformation("SQLite transcript repository initialized. DatabasePath={DatabasePath}", DatabasePath);
            }
            finally
            {
                writeLock.Release();
            }
        }

        public async Task<int> SaveAsync(TranscriptSegment segment, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(segment);

            await InitializeAsync(cancellationToken).ConfigureAwait(false);
            await writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await using var connection = CreateConnection();
                await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
                await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

                var sequenceNo = await GetNextSequenceNoAsync(connection, transaction, segment.CallId, cancellationToken).ConfigureAwait(false);
                await InsertSegmentAsync(connection, transaction, segment, sequenceNo, cancellationToken).ConfigureAwait(false);
                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

                return sequenceNo;
            }
            finally
            {
                writeLock.Release();
            }
        }

        private static string ResolveDatabasePath()
        {
            var configuredPath = Environment.GetEnvironmentVariable(DatabasePathEnvironmentVariable);
            if (!string.IsNullOrWhiteSpace(configuredPath))
            {
                return Path.GetFullPath(configuredPath);
            }

            return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "data", "deciscope.db"));
        }

        private SqliteConnection CreateConnection()
        {
            var builder = new SqliteConnectionStringBuilder
            {
                DataSource = DatabasePath,
            };

            return new SqliteConnection(builder.ToString());
        }

        private static async Task ExecuteNonQueryAsync(SqliteConnection connection, string commandText, CancellationToken cancellationToken)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = commandText;
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        private static async Task EnsureColumnAsync(
            SqliteConnection connection,
            string columnName,
            string columnType,
            CancellationToken cancellationToken)
        {
            await using (var readCommand = connection.CreateCommand())
            {
                readCommand.CommandText = "PRAGMA table_info(transcript_segments);";
                await using var reader = await readCommand.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    if (string.Equals(reader.GetString(1), columnName, StringComparison.OrdinalIgnoreCase))
                    {
                        return;
                    }
                }
            }

            await ExecuteNonQueryAsync(
                connection,
                $"ALTER TABLE transcript_segments ADD COLUMN {columnName} {columnType};",
                cancellationToken).ConfigureAwait(false);
        }

        private static async Task<int> GetNextSequenceNoAsync(
            SqliteConnection connection,
            SqliteTransaction transaction,
            string callId,
            CancellationToken cancellationToken)
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText =
                @"SELECT COALESCE(MAX(sequence_no), 0) + 1
                FROM transcript_segments
                WHERE call_id = $call_id;";
            command.Parameters.AddWithValue("$call_id", callId);

            var value = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            return Convert.ToInt32(value);
        }

        private static async Task InsertSegmentAsync(
            SqliteConnection connection,
            SqliteTransaction transaction,
            TranscriptSegment segment,
            int sequenceNo,
            CancellationToken cancellationToken)
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText =
                @"INSERT INTO transcript_segments (
                    session_id,
                    call_id,
                    speaker_id,
                    speaker_name,
                    sequence_no,
                    recognized_at_utc,
                    offset_ticks,
                    duration_ticks,
                    text,
                    created_at_utc
                )
                VALUES (
                    $session_id,
                    $call_id,
                    $speaker_id,
                    $speaker_name,
                    $sequence_no,
                    $recognized_at_utc,
                    $offset_ticks,
                    $duration_ticks,
                    $text,
                    $created_at_utc
                );";
            command.Parameters.AddWithValue("$session_id", (object?)segment.SessionId ?? DBNull.Value);
            command.Parameters.AddWithValue("$call_id", segment.CallId);
            command.Parameters.AddWithValue("$speaker_id", (object?)segment.SpeakerId ?? DBNull.Value);
            command.Parameters.AddWithValue("$speaker_name", (object?)segment.SpeakerName ?? DBNull.Value);
            command.Parameters.AddWithValue("$sequence_no", sequenceNo);
            command.Parameters.AddWithValue("$recognized_at_utc", segment.RecognizedAtUtc);
            command.Parameters.AddWithValue("$offset_ticks", (object?)segment.OffsetTicks ?? DBNull.Value);
            command.Parameters.AddWithValue("$duration_ticks", (object?)segment.DurationTicks ?? DBNull.Value);
            command.Parameters.AddWithValue("$text", segment.Text);
            command.Parameters.AddWithValue("$created_at_utc", DateTimeOffset.UtcNow.ToString("O"));

            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
    }
}
