using System.Net;
using System.Text.Json;
using EchoBot.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EchoBot.Tests
{
    [TestClass]
    public class BotMeetingStatusReporterHeartbeatTests
    {
        [TestMethod]
        public void HeartbeatInterval_WhenEnabledWithPositiveSeconds_ReturnsTimeSpan()
        {
            var reporter = CreateReporter(EnabledOptions(heartbeatSecondsValue: "30"), new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)));

            Assert.AreEqual(TimeSpan.FromSeconds(30), reporter.HeartbeatInterval);
        }

        [TestMethod]
        public void HeartbeatInterval_WhenHeartbeatSecondsIsZero_ReturnsNull()
        {
            var reporter = CreateReporter(EnabledOptions(heartbeatSecondsValue: "0"), new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)));

            Assert.IsNull(reporter.HeartbeatInterval);
        }

        [TestMethod]
        public void HeartbeatInterval_WhenForwardingDisabled_ReturnsNull()
        {
            var reporter = CreateReporter(
                TranscriptForwardingOptions.FromValues("false", "http://localhost/api/v1/transcript-segments", "secret-key", "5"),
                new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)));

            Assert.IsNull(reporter.HeartbeatInterval);
        }

        [TestMethod]
        public async Task ReportHeartbeatAsync_SendsBotCallIdAndApiKeyHeader()
        {
            var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
            var reporter = CreateReporter(EnabledOptions(), handler);

            await reporter.ReportHeartbeatAsync("session-1", "call-1");

            Assert.AreEqual(1, handler.RequestCount);
            Assert.AreEqual("secret-key", handler.ApiKeyHeader);
            Assert.AreEqual(HttpMethod.Post, handler.LastMethod);

            using var document = JsonDocument.Parse(handler.Body);
            Assert.AreEqual("call-1", document.RootElement.GetProperty("botCallId").GetString());
        }

        [TestMethod]
        public async Task ReportHeartbeatAsync_DoesNotSendWhenSessionIdMissing()
        {
            var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
            var reporter = CreateReporter(EnabledOptions(), handler);

            await reporter.ReportHeartbeatAsync(null, "call-1");

            Assert.AreEqual(0, handler.RequestCount);
        }

        [TestMethod]
        public async Task ReportHeartbeatAsync_DoesNotThrowOnFailureStatus()
        {
            var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
            var reporter = CreateReporter(EnabledOptions(), handler);

            await reporter.ReportHeartbeatAsync("session-1", "call-1");

            Assert.AreEqual(1, handler.RequestCount);
        }

        [TestMethod]
        public async Task ReportHeartbeatAsync_DoesNotThrowOnTimeout()
        {
            var handler = new RecordingHandler(_ => throw new TaskCanceledException("timeout"));
            var reporter = CreateReporter(EnabledOptions(), handler);

            await reporter.ReportHeartbeatAsync("session-1", "call-1");

            Assert.AreEqual(1, handler.RequestCount);
        }

        [TestMethod]
        public async Task ReportHeartbeatAsync_DoesNotRetry()
        {
            var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
            var reporter = CreateReporter(EnabledOptions(), handler);

            await reporter.ReportHeartbeatAsync("session-1", "call-1");

            Assert.AreEqual(1, handler.RequestCount);
        }

        private static BotMeetingStatusReporter CreateReporter(TranscriptForwardingOptions options, RecordingHandler handler)
        {
            return new BotMeetingStatusReporter(
                new TestHttpClientFactory(new HttpClient(handler)),
                options,
                NullLogger<BotMeetingStatusReporter>.Instance);
        }

        private static TranscriptForwardingOptions EnabledOptions(string? heartbeatSecondsValue = null)
        {
            return TranscriptForwardingOptions.FromValues(
                "true",
                "http://localhost/api/v1/transcript-segments",
                "secret-key",
                "5",
                heartbeatSecondsValue: heartbeatSecondsValue);
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

            public string? ApiKeyHeader { get; private set; }

            public HttpMethod? LastMethod { get; private set; }

            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                RequestCount++;
                LastMethod = request.Method;
                ApiKeyHeader = request.Headers.TryGetValues("X-DeciScope-Api-Key", out var values)
                    ? values.SingleOrDefault()
                    : null;

                Body = request.Content == null
                    ? string.Empty
                    : await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

                return responder(request);
            }
        }
    }
}
