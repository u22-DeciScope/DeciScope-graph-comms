using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using EchoBot.Models;

namespace EchoBot.Services
{
    public sealed class TranscriptForwarder : ITranscriptForwarder
    {
        public const string HttpClientName = "TranscriptForwarder";

        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);

        private readonly IHttpClientFactory httpClientFactory;
        private readonly TranscriptForwardingOptions options;
        private readonly ILogger<TranscriptForwarder> logger;

        public TranscriptForwarder(
            IHttpClientFactory httpClientFactory,
            TranscriptForwardingOptions options,
            ILogger<TranscriptForwarder> logger)
        {
            this.httpClientFactory = httpClientFactory;
            this.options = options;
            this.logger = logger;
        }

        public async Task<TranscriptForwardResult> ForwardAsync(
            TranscriptSegment segment,
            int sequenceNo,
            bool isFinal = true,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(segment);

            if (!options.Enabled || options.ApiUrl == null || string.IsNullOrWhiteSpace(options.ApiKey))
            {
                logger.LogInformation(
                    "Transcript forwarding skipped. SessionId={SessionId}; CallId={CallId}; SequenceNo={SequenceNo}; Enabled={Enabled}; ApiUrl={ApiUrl}; ApiKeyConfigured={ApiKeyConfigured}; Reason={Reason}",
                    segment.SessionId,
                    segment.CallId,
                    sequenceNo,
                    options.Enabled,
                    options.ApiUrl,
                    options.ApiKeyConfigured,
                    options.Reason);
                return TranscriptForwardResult.Skipped();
            }

            if (string.IsNullOrWhiteSpace(segment.Text))
            {
                logger.LogInformation(
                    "Transcript forwarding skipped because transcript text is empty. SessionId={SessionId}; CallId={CallId}; SequenceNo={SequenceNo}; TextLength={TextLength}",
                    segment.SessionId,
                    segment.CallId,
                    sequenceNo,
                    segment.Text?.Length ?? 0);
                return TranscriptForwardResult.Skipped();
            }

            var eventId = $"{segment.CallId}:{sequenceNo}";
            if (!isFinal)
            {
                eventId = PartialTranscriptEventId(segment);
            }
            var requestBody = CreateRequest(segment, sequenceNo, eventId, isFinal);
            var json = JsonSerializer.Serialize(requestBody, JsonOptions);
            for (var attempt = 1; attempt <= options.MaxRetryAttempts; attempt++)
            {
                try
                {
                    logger.LogInformation(
                        "Transcript forwarding started. SessionId={SessionId}; CallId={CallId}; SequenceNo={SequenceNo}; ApiUrl={ApiUrl}; TextLength={TextLength}; RetryAttempt={RetryAttempt}",
                        segment.SessionId,
                        segment.CallId,
                        sequenceNo,
                        options.ApiUrl,
                        segment.Text.Length,
                        attempt);

                    using var request = new HttpRequestMessage(HttpMethod.Post, options.ApiUrl);
                    request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                    request.Headers.Add("X-DeciScope-Api-Key", options.ApiKey);
                    request.Content = new StringContent(json, Encoding.UTF8, "application/json");

                    using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    timeoutCts.CancelAfter(TimeSpan.FromSeconds(options.TimeoutSeconds));

                    var client = httpClientFactory.CreateClient(HttpClientName);
                    using var response = await client.SendAsync(request, timeoutCts.Token).ConfigureAwait(false);
                    var duplicate = response.StatusCode == HttpStatusCode.OK && await TryReadDuplicateAsync(response, timeoutCts.Token).ConfigureAwait(false);

                    if (response.StatusCode == HttpStatusCode.Created || response.StatusCode == HttpStatusCode.OK)
                    {
                        logger.LogInformation(
                            "Transcript forwarding succeeded. SessionId={SessionId}; CallId={CallId}; SequenceNo={SequenceNo}; EventId={EventId}; StatusCode={StatusCode}; Duplicate={Duplicate}; RetryAttempt={RetryAttempt}",
                            segment.SessionId,
                            segment.CallId,
                            sequenceNo,
                            eventId,
                            (int)response.StatusCode,
                            duplicate,
                            attempt);
                        return TranscriptForwardResult.Succeeded(response.StatusCode, duplicate);
                    }

                    if (!IsRetryableStatusCode(response.StatusCode) || attempt >= options.MaxRetryAttempts)
                    {
                        LogTerminalHttpFailure(segment, sequenceNo, eventId, response.StatusCode, attempt);
                        return TranscriptForwardResult.Failed(response.StatusCode);
                    }

                    logger.LogWarning(
                        "Transcript forwarding failed. SessionId={SessionId}; CallId={CallId}; SequenceNo={SequenceNo}; EventId={EventId}; StatusCode={StatusCode}; ErrorMessage={ErrorMessage}; RetryAttempt={RetryAttempt}; MaxAttempts={MaxAttempts}",
                        segment.SessionId,
                        segment.CallId,
                        sequenceNo,
                        eventId,
                        (int)response.StatusCode,
                        "Retryable HTTP status code.",
                        attempt,
                        options.MaxRetryAttempts);
                }
                catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
                {
                    if (attempt >= options.MaxRetryAttempts)
                    {
                        logger.LogError(
                            ex,
                            "Transcript forwarding failed. SessionId={SessionId}; CallId={CallId}; SequenceNo={SequenceNo}; EventId={EventId}; StatusCode={StatusCode}; ErrorMessage={ErrorMessage}; RetryAttempt={RetryAttempt}; MaxAttempts={MaxAttempts}; TimeoutSeconds={TimeoutSeconds}",
                            segment.SessionId,
                            segment.CallId,
                            sequenceNo,
                            eventId,
                            null,
                            ex.Message,
                            attempt,
                            options.MaxRetryAttempts,
                            options.TimeoutSeconds);
                        return TranscriptForwardResult.Failed();
                    }

                    logger.LogWarning(
                        ex,
                        "Transcript forwarding failed. SessionId={SessionId}; CallId={CallId}; SequenceNo={SequenceNo}; EventId={EventId}; StatusCode={StatusCode}; ErrorMessage={ErrorMessage}; RetryAttempt={RetryAttempt}; MaxAttempts={MaxAttempts}; TimeoutSeconds={TimeoutSeconds}",
                        segment.SessionId,
                        segment.CallId,
                        sequenceNo,
                        eventId,
                        null,
                        ex.Message,
                        attempt,
                        options.MaxRetryAttempts,
                        options.TimeoutSeconds);
                }
                catch (HttpRequestException ex)
                {
                    if (attempt >= options.MaxRetryAttempts)
                    {
                        logger.LogError(
                            ex,
                            "Transcript forwarding failed. SessionId={SessionId}; CallId={CallId}; SequenceNo={SequenceNo}; EventId={EventId}; StatusCode={StatusCode}; ErrorMessage={ErrorMessage}; RetryAttempt={RetryAttempt}; MaxAttempts={MaxAttempts}",
                            segment.SessionId,
                            segment.CallId,
                            sequenceNo,
                            eventId,
                            null,
                            ex.Message,
                            attempt,
                            options.MaxRetryAttempts);
                        return TranscriptForwardResult.Failed();
                    }

                    logger.LogWarning(
                        ex,
                        "Transcript forwarding failed. SessionId={SessionId}; CallId={CallId}; SequenceNo={SequenceNo}; EventId={EventId}; StatusCode={StatusCode}; ErrorMessage={ErrorMessage}; RetryAttempt={RetryAttempt}; MaxAttempts={MaxAttempts}",
                        segment.SessionId,
                        segment.CallId,
                        sequenceNo,
                        eventId,
                        null,
                        ex.Message,
                        attempt,
                        options.MaxRetryAttempts);
                }
                catch (Exception ex)
                {
                    logger.LogError(
                        ex,
                        "Transcript forwarding failed. SessionId={SessionId}; CallId={CallId}; SequenceNo={SequenceNo}; EventId={EventId}; StatusCode={StatusCode}; ErrorMessage={ErrorMessage}; RetryAttempt={RetryAttempt}",
                        segment.SessionId,
                        segment.CallId,
                        sequenceNo,
                        eventId,
                        null,
                        ex.Message,
                        attempt);
                    return TranscriptForwardResult.Failed();
                }

                logger.LogInformation(
                    "Transcript forwarding retry scheduled. SessionId={SessionId}; CallId={CallId}; SequenceNo={SequenceNo}; RetryAttempt={RetryAttempt}; MaxAttempts={MaxAttempts}",
                    segment.SessionId,
                    segment.CallId,
                    sequenceNo,
                    attempt + 1,
                    options.MaxRetryAttempts);
                await Task.Delay(GetRetryDelay(attempt), cancellationToken).ConfigureAwait(false);
            }

            return TranscriptForwardResult.Failed();
        }

        private static TranscriptForwardRequest CreateRequest(TranscriptSegment segment, int sequenceNo, string eventId, bool isFinal)
        {
            var recognizedAtUtc = DateTimeOffset.TryParse(segment.RecognizedAtUtc, out var parsedRecognizedAtUtc)
                ? parsedRecognizedAtUtc.ToUniversalTime()
                : DateTimeOffset.UtcNow;

            return new TranscriptForwardRequest(
                segment.SessionId,
                eventId,
                segment.CallId,
                segment.SpeakerId,
                segment.SpeakerName,
                sequenceNo,
                recognizedAtUtc,
                segment.OffsetTicks,
                segment.DurationTicks,
                segment.Text,
                isFinal);
        }

        private static string PartialTranscriptEventId(TranscriptSegment segment)
        {
            var speakerKey = !string.IsNullOrWhiteSpace(segment.SpeakerId)
                ? segment.SpeakerId
                : !string.IsNullOrWhiteSpace(segment.SpeakerName)
                    ? segment.SpeakerName
                    : "unknown";
            return $"partial:{segment.CallId}:{speakerKey}";
        }

        private static async Task<bool> TryReadDuplicateAsync(HttpResponseMessage response, CancellationToken cancellationToken)
        {
            try
            {
                await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                if (response.Content.Headers.ContentLength == 0)
                {
                    return false;
                }

                using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
                return document.RootElement.TryGetProperty("duplicate", out var duplicate)
                    && duplicate.ValueKind == JsonValueKind.True;
            }
            catch
            {
                return false;
            }
        }

        private static bool IsRetryableStatusCode(HttpStatusCode statusCode)
        {
            return statusCode == (HttpStatusCode)429
                || statusCode == HttpStatusCode.InternalServerError
                || statusCode == HttpStatusCode.BadGateway
                || statusCode == HttpStatusCode.ServiceUnavailable
                || statusCode == HttpStatusCode.GatewayTimeout;
        }

        private static TimeSpan GetRetryDelay(int attempt)
        {
            return TimeSpan.FromMilliseconds(250 * Math.Pow(2, attempt - 1));
        }

        private void LogTerminalHttpFailure(TranscriptSegment segment, int sequenceNo, string eventId, HttpStatusCode statusCode, int attempt)
        {
            var status = (int)statusCode;
            if (statusCode == HttpStatusCode.BadRequest)
            {
                logger.LogError(
                    "Transcript forwarding failed. SessionId={SessionId}; CallId={CallId}; SequenceNo={SequenceNo}; EventId={EventId}; StatusCode={StatusCode}; ErrorMessage={ErrorMessage}; RetryAttempt={RetryAttempt}",
                    segment.SessionId,
                    segment.CallId,
                    sequenceNo,
                    eventId,
                    status,
                    "Go API rejected transcript payload.",
                    attempt);
                return;
            }

            if (statusCode == HttpStatusCode.Unauthorized || statusCode == HttpStatusCode.Forbidden)
            {
                logger.LogError(
                    "Transcript forwarding failed. SessionId={SessionId}; CallId={CallId}; SequenceNo={SequenceNo}; EventId={EventId}; StatusCode={StatusCode}; ErrorMessage={ErrorMessage}; RetryAttempt={RetryAttempt}",
                    segment.SessionId,
                    segment.CallId,
                    sequenceNo,
                    eventId,
                    status,
                    "Go API rejected transcript API authentication. Check DECISCOPE_TRANSCRIPT_API_KEY and Go DECISCOPE_INGEST_API_KEY.",
                    attempt);
                return;
            }

            if (statusCode == HttpStatusCode.Conflict)
            {
                logger.LogWarning(
                    "Transcript forwarding failed. SessionId={SessionId}; CallId={CallId}; SequenceNo={SequenceNo}; EventId={EventId}; StatusCode={StatusCode}; ErrorMessage={ErrorMessage}; RetryAttempt={RetryAttempt}",
                    segment.SessionId,
                    segment.CallId,
                    sequenceNo,
                    eventId,
                    status,
                    "Go API reported a transcript conflict. Confirm whether 409 means duplicate already stored.",
                    attempt);
                return;
            }

            logger.LogWarning(
                "Transcript forwarding failed. SessionId={SessionId}; CallId={CallId}; SequenceNo={SequenceNo}; EventId={EventId}; StatusCode={StatusCode}; ErrorMessage={ErrorMessage}; RetryAttempt={RetryAttempt}",
                segment.SessionId,
                segment.CallId,
                sequenceNo,
                eventId,
                status,
                "Go API rejected transcript forwarding request.",
                attempt);
        }
    }
}
