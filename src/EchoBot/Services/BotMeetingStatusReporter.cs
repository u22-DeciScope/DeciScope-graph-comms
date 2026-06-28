using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace EchoBot.Services
{
    public sealed class BotMeetingStatusReporter : IBotMeetingStatusReporter
    {
        public const string HttpClientName = "BotMeetingStatusReporter";

        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        private const int MaxAttempts = 3;

        private readonly IHttpClientFactory httpClientFactory;
        private readonly TranscriptForwardingOptions options;
        private readonly ILogger<BotMeetingStatusReporter> logger;

        public BotMeetingStatusReporter(
            IHttpClientFactory httpClientFactory,
            TranscriptForwardingOptions options,
            ILogger<BotMeetingStatusReporter> logger)
        {
            this.httpClientFactory = httpClientFactory;
            this.options = options;
            this.logger = logger;
        }

        public async Task ReportAsync(
            string? sessionId,
            string status,
            string message,
            string? botCallId = null,
            CancellationToken cancellationToken = default,
            string? failedReason = null,
            string? errorCode = null,
            string? source = null)
        {
            if (string.IsNullOrWhiteSpace(sessionId)
                || !options.Enabled
                || options.ApiUrl == null
                || string.IsNullOrWhiteSpace(options.ApiKey))
            {
                return;
            }

            var statusUrl = BuildStatusUrl(options.ApiUrl, sessionId);
            var body = new BotMeetingStatusUpdate(status, botCallId, message, failedReason, errorCode, source);
            var json = JsonSerializer.Serialize(body, JsonOptions);

            for (var attempt = 1; attempt <= MaxAttempts; attempt++)
            {
                try
                {
                    using var request = new HttpRequestMessage(HttpMethod.Patch, statusUrl);
                    request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                    request.Headers.Add("X-DeciScope-Api-Key", options.ApiKey);
                    request.Content = new StringContent(json, Encoding.UTF8, "application/json");

                    using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    timeoutCts.CancelAfter(TimeSpan.FromSeconds(options.TimeoutSeconds));

                    var client = httpClientFactory.CreateClient(HttpClientName);
                    using var response = await client.SendAsync(request, timeoutCts.Token).ConfigureAwait(false);

                    if (!response.IsSuccessStatusCode)
                    {
                        logger.LogWarning(
                            "Status report failed. SessionId={SessionId}; Status={Status}; BotCallId={BotCallId}; FailedReason={FailedReason}; ErrorCode={ErrorCode}; Source={Source}; StatusCode={StatusCode}; Attempt={Attempt}; MaxAttempts={MaxAttempts}",
                            sessionId,
                            status,
                            botCallId,
                            failedReason,
                            errorCode,
                            source,
                            (int)response.StatusCode,
                            attempt,
                            MaxAttempts);

                        if (attempt < MaxAttempts)
                        {
                            await DelayBeforeRetryAsync(attempt, cancellationToken).ConfigureAwait(false);
                            continue;
                        }

                        return;
                    }

                    logger.LogInformation(
                        "Status report succeeded. SessionId={SessionId}; Status={Status}; BotCallId={BotCallId}; FailedReason={FailedReason}; ErrorCode={ErrorCode}; Source={Source}; StatusCode={StatusCode}; Attempt={Attempt}",
                        sessionId,
                        status,
                        botCallId,
                        failedReason,
                        errorCode,
                        source,
                        (int)response.StatusCode,
                        attempt);
                    return;
                }
                catch (Exception ex) when (ex is HttpRequestException || ex is TaskCanceledException || ex is OperationCanceledException)
                {
                    logger.LogWarning(
                        ex,
                        "Status report retry. SessionId={SessionId}; Status={Status}; BotCallId={BotCallId}; FailedReason={FailedReason}; ErrorCode={ErrorCode}; Source={Source}; Attempt={Attempt}; MaxAttempts={MaxAttempts}; Error={Error}",
                        sessionId,
                        status,
                        botCallId,
                        failedReason,
                        errorCode,
                        source,
                        attempt,
                        MaxAttempts,
                        ex.Message);

                    if (attempt >= MaxAttempts)
                    {
                        logger.LogError(
                            ex,
                            "Status report failed. SessionId={SessionId}; Status={Status}; BotCallId={BotCallId}; FailedReason={FailedReason}; ErrorCode={ErrorCode}; Source={Source}; Attempts={Attempts}; Error={Error}",
                            sessionId,
                            status,
                            botCallId,
                            failedReason,
                            errorCode,
                            source,
                            attempt,
                            ex.Message);
                        return;
                    }

                    await DelayBeforeRetryAsync(attempt, cancellationToken).ConfigureAwait(false);
                }
            }
        }

        private static async Task DelayBeforeRetryAsync(int attempt, CancellationToken cancellationToken)
        {
            try
            {
                await Task.Delay(TimeSpan.FromMilliseconds(250 * attempt), cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }

        public static Uri BuildStatusUrl(Uri transcriptApiUrl, string sessionId)
        {
            var escapedSessionId = Uri.EscapeDataString(sessionId);
            var builder = new UriBuilder(transcriptApiUrl);
            var path = builder.Path;
            var apiV1Index = path.IndexOf("/api/v1/", StringComparison.OrdinalIgnoreCase);

            builder.Path = apiV1Index >= 0
                ? path.Substring(0, apiV1Index) + $"/api/v1/bot/meeting-sessions/{escapedSessionId}/status"
                : $"/api/v1/bot/meeting-sessions/{escapedSessionId}/status";
            builder.Query = string.Empty;
            return builder.Uri;
        }
    }
}
