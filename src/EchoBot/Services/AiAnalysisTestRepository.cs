using Microsoft.Data.Sqlite;

namespace EchoBot.Services
{
    public sealed class AiAnalysisTestRepository
    {
        private readonly ITranscriptRepository transcriptRepository;
        private readonly ILogger<AiAnalysisTestRepository> logger;
        private readonly SemaphoreSlim writeLock = new SemaphoreSlim(1, 1);
        private bool initialized;

        public AiAnalysisTestRepository(
            ITranscriptRepository transcriptRepository,
            ILogger<AiAnalysisTestRepository> logger)
        {
            this.transcriptRepository = transcriptRepository;
            this.logger = logger;
        }

        public async Task<int> SaveAsync(
            string deploymentName,
            string inputText,
            MeetingAnalysisResult result,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(deploymentName))
            {
                throw new ArgumentException("Deployment name is required.", nameof(deploymentName));
            }

            if (string.IsNullOrWhiteSpace(inputText))
            {
                throw new ArgumentException("Input text is required.", nameof(inputText));
            }
            ArgumentNullException.ThrowIfNull(result);

            await InitializeAsync(cancellationToken).ConfigureAwait(false);
            await writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await using var connection = CreateConnection();
                await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
                await using var command = connection.CreateCommand();
                command.CommandText =
                    @"INSERT INTO ai_analysis_tests (
                        deployment_name,
                        input_text,
                        output_text,
                        raw_response_json,
                        status_code,
                        error_message,
                        created_at
                    )
                    VALUES (
                        $deployment_name,
                        $input_text,
                        $output_text,
                        $raw_response_json,
                        $status_code,
                        $error_message,
                        $created_at
                    );
                    SELECT last_insert_rowid();";
                command.Parameters.AddWithValue("$deployment_name", deploymentName);
                command.Parameters.AddWithValue("$input_text", inputText);
                command.Parameters.AddWithValue("$output_text", (object?)result.OutputText ?? DBNull.Value);
                command.Parameters.AddWithValue("$raw_response_json", (object?)result.RawResponseJson ?? DBNull.Value);
                command.Parameters.AddWithValue("$status_code", (object?)result.StatusCode ?? DBNull.Value);
                command.Parameters.AddWithValue("$error_message", (object?)result.ErrorMessage ?? DBNull.Value);
                command.Parameters.AddWithValue("$created_at", DateTimeOffset.UtcNow.ToString("O"));

                var id = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false));
                logger.LogInformation("saved ai_analysis_tests id. Id={Id}; DeploymentName={DeploymentName}", id, deploymentName);
                return id;
            }
            finally
            {
                writeLock.Release();
            }
        }

        private async Task InitializeAsync(CancellationToken cancellationToken)
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

                await transcriptRepository.InitializeAsync(cancellationToken).ConfigureAwait(false);
                await using var connection = CreateConnection();
                await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
                await using var command = connection.CreateCommand();
                command.CommandText =
                    @"CREATE TABLE IF NOT EXISTS ai_analysis_tests (
                        id                INTEGER PRIMARY KEY AUTOINCREMENT,
                        deployment_name   TEXT NOT NULL,
                        input_text        TEXT NOT NULL,
                        output_text       TEXT,
                        raw_response_json TEXT,
                        status_code       INTEGER,
                        error_message     TEXT,
                        created_at        TEXT NOT NULL
                    );";
                await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                initialized = true;
                logger.LogInformation("SQLite ai_analysis_tests repository initialized. DatabasePath={DatabasePath}", transcriptRepository.DatabasePath);
            }
            finally
            {
                writeLock.Release();
            }
        }

        private SqliteConnection CreateConnection()
        {
            var builder = new SqliteConnectionStringBuilder
            {
                DataSource = transcriptRepository.DatabasePath,
            };

            return new SqliteConnection(builder.ToString());
        }
    }
}
