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
            try
            {
                var requestBody = CreateRequest(segment, sequenceNo, eventId);
                var json = JsonSerializer.Serialize(requestBody, JsonOptions);
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
                        "Transcript forwarded to Go API. CallId={CallId}; SequenceNo={SequenceNo}; EventId={EventId}; StatusCode={StatusCode}; Duplicate={Duplicate}",
                        segment.CallId,
                        sequenceNo,
                        eventId,
                        (int)response.StatusCode,
                        duplicate);
                    return TranscriptForwardResult.Succeeded(response.StatusCode, duplicate);
                }

                logger.LogWarning(
                    "Failed to forward transcript to Go API. CallId={CallId}; SequenceNo={SequenceNo}; EventId={EventId}; StatusCode={StatusCode}",
                    segment.CallId,
                    sequenceNo,
                    eventId,
                    (int)response.StatusCode);
                return TranscriptForwardResult.Failed(response.StatusCode);
            }
            catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
            {
                logger.LogError(
                    ex,
                    "Timed out forwarding transcript to Go API. CallId={CallId}; SequenceNo={SequenceNo}; EventId={EventId}; TimeoutSeconds={TimeoutSeconds}",
                    segment.CallId,
                    sequenceNo,
                    eventId,
                    options.TimeoutSeconds);
                return TranscriptForwardResult.Failed();
            }
            catch (Exception ex)
            {
                logger.LogError(
                    ex,
                    "Failed to forward transcript to Go API. CallId={CallId}; SequenceNo={SequenceNo}; EventId={EventId}",
                    segment.CallId,
                    sequenceNo,
                    eventId);
                return TranscriptForwardResult.Failed();
            }
        }

        private static TranscriptForwardRequest CreateRequest(TranscriptSegment segment, int sequenceNo, string eventId)
        {
            var recognizedAtUtc = DateTimeOffset.TryParse(segment.RecognizedAtUtc, out var parsedRecognizedAtUtc)
                ? parsedRecognizedAtUtc.ToUniversalTime()
                : DateTimeOffset.UtcNow;

            return new TranscriptForwardRequest(
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
                if (stream.Length == 0)
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
    }
}
