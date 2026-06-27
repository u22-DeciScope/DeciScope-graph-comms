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
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(segment);

            if (!options.Enabled || options.ApiUrl == null || string.IsNullOrWhiteSpace(options.ApiKey))
            {
                return TranscriptForwardResult.Skipped();
            }

            var eventId = $"{segment.CallId}:{sequenceNo}";
            var requestBody = CreateRequest(segment, sequenceNo, eventId);
            var json = JsonSerializer.Serialize(requestBody, JsonOptions);
            for (var attempt = 1; attempt <= options.MaxRetryAttempts; attempt++)
            {
                try
                {
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
                            "Transcript forwarded to Go API. CallId={CallId}; SequenceNo={SequenceNo}; EventId={EventId}; StatusCode={StatusCode}; Duplicate={Duplicate}; Attempt={Attempt}",
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
                        LogTerminalHttpFailure(segment.CallId, sequenceNo, eventId, response.StatusCode, attempt);
                        return TranscriptForwardResult.Failed(response.StatusCode);
                    }

                    logger.LogWarning(
                        "Retryable failure forwarding transcript to Go API. CallId={CallId}; SequenceNo={SequenceNo}; EventId={EventId}; StatusCode={StatusCode}; Attempt={Attempt}; MaxAttempts={MaxAttempts}",
                        segment.CallId,
                        sequenceNo,
                        eventId,
                        (int)response.StatusCode,
                        attempt,
                        options.MaxRetryAttempts);
                }
                catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
                {
                    if (attempt >= options.MaxRetryAttempts)
                    {
                        logger.LogError(
                            ex,
                            "Timed out forwarding transcript to Go API. CallId={CallId}; SequenceNo={SequenceNo}; EventId={EventId}; TimeoutSeconds={TimeoutSeconds}; Attempt={Attempt}; MaxAttempts={MaxAttempts}",
                            segment.CallId,
                            sequenceNo,
                            eventId,
                            options.TimeoutSeconds,
                            attempt,
                            options.MaxRetryAttempts);
                        return TranscriptForwardResult.Failed();
                    }

                    logger.LogWarning(
                        ex,
                        "Timed out forwarding transcript to Go API; retrying. CallId={CallId}; SequenceNo={SequenceNo}; EventId={EventId}; TimeoutSeconds={TimeoutSeconds}; Attempt={Attempt}; MaxAttempts={MaxAttempts}",
                        segment.CallId,
                        sequenceNo,
                        eventId,
                        options.TimeoutSeconds,
                        attempt,
                        options.MaxRetryAttempts);
                }
                catch (HttpRequestException ex)
                {
                    if (attempt >= options.MaxRetryAttempts)
                    {
                        logger.LogError(
                            ex,
                            "Failed to connect to Go API. CallId={CallId}; SequenceNo={SequenceNo}; EventId={EventId}; Attempt={Attempt}; MaxAttempts={MaxAttempts}",
                            segment.CallId,
                            sequenceNo,
                            eventId,
                            attempt,
                            options.MaxRetryAttempts);
                        return TranscriptForwardResult.Failed();
                    }

                    logger.LogWarning(
                        ex,
                        "Failed to connect to Go API; retrying. CallId={CallId}; SequenceNo={SequenceNo}; EventId={EventId}; Attempt={Attempt}; MaxAttempts={MaxAttempts}",
                        segment.CallId,
                        sequenceNo,
                        eventId,
                        attempt,
                        options.MaxRetryAttempts);
                }
                catch (Exception ex)
                {
                    logger.LogError(
                        ex,
                        "Failed to forward transcript to Go API. CallId={CallId}; SequenceNo={SequenceNo}; EventId={EventId}; Attempt={Attempt}",
                        segment.CallId,
                        sequenceNo,
                        eventId,
                        attempt);
                    return TranscriptForwardResult.Failed();
                }

                await Task.Delay(GetRetryDelay(attempt), cancellationToken).ConfigureAwait(false);
            }

            return TranscriptForwardResult.Failed();
        }

        private static TranscriptForwardRequest CreateRequest(TranscriptSegment segment, int sequenceNo, string eventId)
        {
            var recognizedAtUtc = DateTimeOffset.TryParse(segment.RecognizedAtUtc, out var parsedRecognizedAtUtc)
                ? parsedRecognizedAtUtc.ToUniversalTime()
                : DateTimeOffset.UtcNow;

            return new TranscriptForwardRequest(
                segment.SessionId,
                eventId,
                segment.CallId,
                sequenceNo,
                recognizedAtUtc,
                segment.OffsetTicks,
                segment.DurationTicks,
                segment.Text);
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

        private void LogTerminalHttpFailure(string callId, int sequenceNo, string eventId, HttpStatusCode statusCode, int attempt)
        {
            var status = (int)statusCode;
            if (statusCode == HttpStatusCode.BadRequest)
            {
                logger.LogError(
                    "Go API rejected transcript payload. CallId={CallId}; SequenceNo={SequenceNo}; EventId={EventId}; StatusCode={StatusCode}; Attempt={Attempt}",
                    callId,
                    sequenceNo,
                    eventId,
                    status,
                    attempt);
                return;
            }

            if (statusCode == HttpStatusCode.Unauthorized || statusCode == HttpStatusCode.Forbidden)
            {
                logger.LogError(
                    "Go API rejected transcript API authentication. Check DECISCOPE_TRANSCRIPT_API_KEY and Go DECISCOPE_INGEST_API_KEY. CallId={CallId}; SequenceNo={SequenceNo}; EventId={EventId}; StatusCode={StatusCode}; Attempt={Attempt}",
                    callId,
                    sequenceNo,
                    eventId,
                    status,
                    attempt);
                return;
            }

            if (statusCode == HttpStatusCode.Conflict)
            {
                logger.LogWarning(
                    "Go API reported a transcript conflict. Confirm whether 409 means duplicate already stored. CallId={CallId}; SequenceNo={SequenceNo}; EventId={EventId}; StatusCode={StatusCode}; Attempt={Attempt}",
                    callId,
                    sequenceNo,
                    eventId,
                    status,
                    attempt);
                return;
            }

            logger.LogWarning(
                "Failed to forward transcript to Go API. CallId={CallId}; SequenceNo={SequenceNo}; EventId={EventId}; StatusCode={StatusCode}; Attempt={Attempt}",
                callId,
                sequenceNo,
                eventId,
                status,
                attempt);
        }
    }
}
