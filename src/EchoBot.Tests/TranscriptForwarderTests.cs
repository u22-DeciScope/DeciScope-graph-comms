using System.Net;
using System.Text.Json;
using EchoBot.Models;
using EchoBot.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EchoBot.Tests
{
    [TestClass]
    public class TranscriptForwarderTests
    {
        [TestMethod]
        public async Task ForwardAsync_DoesNotSendWhenDisabled()
        {
            var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.Created));
            var forwarder = CreateForwarder(
                TranscriptForwardingOptions.FromValues("false", "http://localhost/api", "secret", "5"),
                handler);

            var result = await forwarder.ForwardAsync(CreateSegment(), 7);

            Assert.IsFalse(result.Attempted);
            Assert.AreEqual(0, handler.RequestCount);
        }

        [TestMethod]
        public async Task ForwardAsync_TreatsCreatedAsSuccess()
        {
            var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.Created));
            var forwarder = CreateForwarder(EnabledOptions(), handler);

            var result = await forwarder.ForwardAsync(CreateSegment(), 7);

            Assert.IsTrue(result.Success);
            Assert.AreEqual(HttpStatusCode.Created, result.StatusCode);
        }

        [TestMethod]
        public async Task ForwardAsync_TreatsOkAsSuccessAndReadsDuplicate()
        {
            var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"duplicate\":true}"),
            });
            var forwarder = CreateForwarder(EnabledOptions(), handler);

            var result = await forwarder.ForwardAsync(CreateSegment(), 7);

            Assert.IsTrue(result.Success);
            Assert.AreEqual(HttpStatusCode.OK, result.StatusCode);
            Assert.IsTrue(result.Duplicate);
        }

        [TestMethod]
        public async Task ForwardAsync_SendsApiKeyHeader()
        {
            var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.Created));
            var forwarder = CreateForwarder(EnabledOptions(), handler);

            await forwarder.ForwardAsync(CreateSegment(), 7);

            Assert.AreEqual("secret-key", handler.ApiKeyHeader);
        }

        [TestMethod]
        public async Task ForwardAsync_SendsCamelCaseJsonWithEventIdAndTicks()
        {
            var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.Created));
            var forwarder = CreateForwarder(EnabledOptions(), handler);

            await forwarder.ForwardAsync(CreateSegment(), 7);

            using var document = JsonDocument.Parse(handler.Body);
            var root = document.RootElement;
            Assert.AreEqual("call-1:7", root.GetProperty("eventId").GetString());
            Assert.AreEqual("call-1", root.GetProperty("callId").GetString());
            Assert.AreEqual("8", root.GetProperty("speakerId").GetString());
            Assert.AreEqual("佐藤さん", root.GetProperty("speakerName").GetString());
            Assert.AreEqual(7, root.GetProperty("sequenceNo").GetInt32());
            Assert.AreEqual(357600000, root.GetProperty("offsetTicks").GetInt64());
            Assert.AreEqual(10400000, root.GetProperty("durationTicks").GetInt64());
            Assert.AreEqual("大丈夫っすか？", root.GetProperty("text").GetString());
            Assert.IsTrue(root.GetProperty("isFinal").GetBoolean());
            Assert.IsTrue(root.TryGetProperty("recognizedAtUtc", out _));
        }

        [TestMethod]
        public async Task ForwardAsync_SendsPartialWithIsFinalFalse()
        {
            var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
            var forwarder = CreateForwarder(EnabledOptions(), handler);

            await forwarder.ForwardAsync(CreateSegment("session-1"), 0, isFinal: false);

            using var document = JsonDocument.Parse(handler.Body);
            var root = document.RootElement;
            Assert.AreEqual("partial:call-1:8", root.GetProperty("eventId").GetString());
            Assert.AreEqual("session-1", root.GetProperty("sessionId").GetString());
            Assert.AreEqual(0, root.GetProperty("sequenceNo").GetInt32());
            Assert.IsFalse(root.GetProperty("isFinal").GetBoolean());
        }

        [TestMethod]
        public async Task ForwardAsync_IncludesSessionIdWhenProvided()
        {
            var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.Created));
            var forwarder = CreateForwarder(EnabledOptions(), handler);

            await forwarder.ForwardAsync(CreateSegment("session-1"), 7);

            using var document = JsonDocument.Parse(handler.Body);
            Assert.AreEqual("session-1", document.RootElement.GetProperty("sessionId").GetString());
        }

        [TestMethod]
        public async Task ForwardAsync_OmitsSessionIdWhenMissing()
        {
            var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.Created));
            var forwarder = CreateForwarder(EnabledOptions(), handler);

            await forwarder.ForwardAsync(CreateSegment(), 7);

            using var document = JsonDocument.Parse(handler.Body);
            Assert.IsFalse(document.RootElement.TryGetProperty("sessionId", out _));
        }

        [TestMethod]
        public async Task ForwardAsync_ReturnsFailureForUnauthorized()
        {
            var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized));
            var forwarder = CreateForwarder(EnabledOptions(), handler);

            var result = await forwarder.ForwardAsync(CreateSegment(), 7);

            Assert.IsTrue(result.Attempted);
            Assert.IsFalse(result.Success);
            Assert.AreEqual(HttpStatusCode.Unauthorized, result.StatusCode);
            Assert.AreEqual(1, handler.RequestCount);
        }

        [TestMethod]
        public async Task ForwardAsync_ReturnsFailureForConflict()
        {
            var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.Conflict));
            var forwarder = CreateForwarder(EnabledOptions(), handler);

            var result = await forwarder.ForwardAsync(CreateSegment(), 7);

            Assert.IsTrue(result.Attempted);
            Assert.IsFalse(result.Success);
            Assert.AreEqual(HttpStatusCode.Conflict, result.StatusCode);
            Assert.AreEqual(1, handler.RequestCount);
        }

        [TestMethod]
        public async Task ForwardAsync_RetriesServiceUnavailableWithSamePayload()
        {
            var attempts = 0;
            var handler = new RecordingHandler(_ =>
                Interlocked.Increment(ref attempts) == 1
                    ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
                    : new HttpResponseMessage(HttpStatusCode.Created));
            var forwarder = CreateForwarder(TranscriptForwardingOptions.FromValues("true", "http://localhost/api/v1/transcript-segments", "secret-key", "5", "2"), handler);

            var result = await forwarder.ForwardAsync(CreateSegment(), 7);

            Assert.IsTrue(result.Success);
            Assert.AreEqual(2, handler.RequestCount);
            Assert.AreEqual(1, handler.DistinctBodies.Count);
        }

        [TestMethod]
        public async Task ForwardAsync_DoesNotThrowOnTimeout()
        {
            var handler = new RecordingHandler(_ => throw new TaskCanceledException("timeout"));
            var forwarder = CreateForwarder(EnabledOptions(), handler);

            var result = await forwarder.ForwardAsync(CreateSegment(), 7);

            Assert.IsTrue(result.Attempted);
            Assert.IsFalse(result.Success);
        }

        [TestMethod]
        public async Task ForwardAsync_DoesNotLogApiKey()
        {
            var logger = new RecordingLogger<TranscriptForwarder>();
            var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized));
            var forwarder = CreateForwarder(EnabledOptions(), handler, logger);

            await forwarder.ForwardAsync(CreateSegment(), 7);

            Assert.IsFalse(logger.Messages.Any(message => message.Contains("secret-key", StringComparison.Ordinal)));
        }

        private static TranscriptForwarder CreateForwarder(
            TranscriptForwardingOptions options,
            RecordingHandler handler,
            ILogger<TranscriptForwarder>? logger = null)
        {
            return new TranscriptForwarder(
                new TestHttpClientFactory(new HttpClient(handler)),
                options,
                logger ?? NullLogger<TranscriptForwarder>.Instance);
        }

        private static TranscriptForwardingOptions EnabledOptions()
        {
            return TranscriptForwardingOptions.FromValues("true", "http://localhost/api/v1/transcript-segments", "secret-key", "5");
        }

        private static TranscriptSegment CreateSegment(string? sessionId = null)
        {
            return new TranscriptSegment
            {
                SessionId = sessionId,
                CallId = "call-1",
                SpeakerId = "8",
                SpeakerName = "佐藤さん",
                RecognizedAtUtc = "2026-06-25T15:20:01.1234567Z",
                OffsetTicks = 357600000,
                DurationTicks = 10400000,
                Text = "大丈夫っすか？",
            };
        }

        private sealed class TestHttpClientFactory : IHttpClientFactory
        {
            private readonly HttpClient client;

            public TestHttpClientFactory(HttpClient client)
            {
                this.client = client;
            }

            public HttpClient CreateClient(string name) => client;
        }

        private sealed class RecordingHandler : HttpMessageHandler
        {
            private readonly Func<HttpRequestMessage, HttpResponseMessage> responder;

            public RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
            {
                this.responder = responder;
            }

            public int RequestCount { get; private set; }

            public string Body { get; private set; } = string.Empty;

            public HashSet<string> DistinctBodies { get; } = new HashSet<string>();

            public string? ApiKeyHeader { get; private set; }

            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                RequestCount++;
                ApiKeyHeader = request.Headers.TryGetValues("X-DeciScope-Api-Key", out var values)
                    ? values.SingleOrDefault()
                    : null;

                Body = request.Content == null
                    ? string.Empty
                    : await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                DistinctBodies.Add(Body);

                return responder(request);
            }
        }

        private sealed class RecordingLogger<T> : ILogger<T>
        {
            public List<string> Messages { get; } = new List<string>();

            public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                Messages.Add(formatter(state, exception));
                if (exception != null)
                {
                    Messages.Add(exception.ToString());
                }
            }
        }

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new NullScope();

            public void Dispose()
            {
            }
        }
    }
}
