using System.Net;
using EchoBot.Models;
using EchoBot.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EchoBot.Tests
{
    [TestClass]
    public class QueuedTranscriptForwarderDrainTests
    {
        private static TranscriptForwardingOptions EnabledOptions()
        {
            return TranscriptForwardingOptions.FromValues(
                "true",
                "http://localhost/api/v1/transcript-segments",
                "secret-key",
                "5");
        }

        private static QueuedTranscriptForwarder CreateStartedForwarder(
            HttpMessageHandler handler,
            out TranscriptForwarder sender)
        {
            var options = EnabledOptions();
            sender = new TranscriptForwarder(
                new TestHttpClientFactory(new HttpClient(handler)),
                options,
                NullLogger<TranscriptForwarder>.Instance);
            var queued = new QueuedTranscriptForwarder(sender, options, NullLogger<QueuedTranscriptForwarder>.Instance);
            queued.StartAsync(CancellationToken.None).GetAwaiter().GetResult();
            return queued;
        }

        private static TranscriptSegment CreateSegment(string sessionId, string text = "最後の発言です。")
        {
            return new TranscriptSegment
            {
                SessionId = sessionId,
                CallId = "call-1",
                SpeakerId = "8",
                SpeakerName = "佐藤さん",
                RecognizedAtUtc = "2026-07-12T15:20:01.1234567Z",
                OffsetTicks = 357600000,
                DurationTicks = 10400000,
                Text = text,
            };
        }

        [TestMethod]
        public async Task DrainSessionAsync_CompletesAfterHttpForwardSucceeds()
        {
            var handler = new ScriptedHandler(_ => new HttpResponseMessage(HttpStatusCode.Created));
            var queued = CreateStartedForwarder(handler, out _);
            try
            {
                var result = await queued.ForwardAsync(CreateSegment("session-a"), 27);
                Assert.IsTrue(result.Queued, "enqueue自体は成功する");

                var drain = await queued.DrainSessionAsync("session-a", new CancellationTokenSource(TimeSpan.FromSeconds(5)).Token);

                Assert.IsTrue(drain.Drained);
                Assert.AreEqual(27L, drain.LastFinalSequenceNo);
                Assert.AreEqual(0, drain.PendingCount);
                Assert.AreEqual(0, drain.FailedCount);
            }
            finally
            {
                await queued.StopAsync(CancellationToken.None);
            }
        }

        [TestMethod]
        public async Task DrainSessionAsync_QueuedItemAloneDoesNotAdvanceLastFinalSequence()
        {
            // HTTP応答を保留したままdrainをtimeoutさせる: queueへ追加しただけの
            // itemは転送成功とみなされない。
            var gate = new SemaphoreSlim(0);
            var handler = new ScriptedHandler(_ =>
            {
                gate.Wait(TimeSpan.FromSeconds(10));
                return new HttpResponseMessage(HttpStatusCode.Created);
            });
            var queued = CreateStartedForwarder(handler, out _);
            try
            {
                await queued.ForwardAsync(CreateSegment("session-a"), 27);

                var drain = await queued.DrainSessionAsync("session-a", new CancellationTokenSource(TimeSpan.FromMilliseconds(300)).Token);

                Assert.IsFalse(drain.Drained, "転送未完了のうちはdrain完了にならない");
                Assert.IsNull(drain.LastFinalSequenceNo, "queueへ追加しただけのsequenceを最終送信成功として報告しない");
                Assert.AreEqual(1, drain.PendingCount);
            }
            finally
            {
                gate.Release(10);
                await queued.StopAsync(CancellationToken.None);
            }
        }

        [TestMethod]
        public async Task DrainSessionAsync_WaitsThroughRetryUntilSuccess()
        {
            var attempts = 0;
            var handler = new ScriptedHandler(_ =>
            {
                attempts++;
                return attempts == 1
                    ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
                    : new HttpResponseMessage(HttpStatusCode.Created);
            });
            var queued = CreateStartedForwarder(handler, out _);
            try
            {
                await queued.ForwardAsync(CreateSegment("session-a"), 27);

                var drain = await queued.DrainSessionAsync("session-a", new CancellationTokenSource(TimeSpan.FromSeconds(10)).Token);

                Assert.IsTrue(drain.Drained, "retry成功までdrain完了を待つ");
                Assert.AreEqual(27L, drain.LastFinalSequenceNo);
                Assert.AreEqual(0, drain.FailedCount);
                Assert.AreEqual(2, attempts);
            }
            finally
            {
                await queued.StopAsync(CancellationToken.None);
            }
        }

        [TestMethod]
        public async Task DrainSessionAsync_ReportsOnlySucceededSequenceAfterTerminalFailure()
        {
            // seq=27は成功、seq=28は終端失敗(400) → drainは完了するが、
            // lastFinalSequenceNoは成功した27のまま。
            var handler = new ScriptedHandler(request =>
            {
                var body = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
                return body.Contains("\"sequenceNo\":28")
                    ? new HttpResponseMessage(HttpStatusCode.BadRequest)
                    : new HttpResponseMessage(HttpStatusCode.Created);
            });
            var queued = CreateStartedForwarder(handler, out _);
            try
            {
                await queued.ForwardAsync(CreateSegment("session-a"), 27);
                await queued.ForwardAsync(CreateSegment("session-a", "保存できない発言"), 28);

                var drain = await queued.DrainSessionAsync("session-a", new CancellationTokenSource(TimeSpan.FromSeconds(10)).Token);

                Assert.IsTrue(drain.Drained);
                Assert.AreEqual(27L, drain.LastFinalSequenceNo, "実際に成功した最大sequenceだけを報告する");
                Assert.AreEqual(1, drain.FailedCount);
            }
            finally
            {
                await queued.StopAsync(CancellationToken.None);
            }
        }

        [TestMethod]
        public async Task DrainSessionAsync_TracksSessionsIndependently()
        {
            var handler = new ScriptedHandler(_ => new HttpResponseMessage(HttpStatusCode.Created));
            var queued = CreateStartedForwarder(handler, out _);
            try
            {
                await queued.ForwardAsync(CreateSegment("session-a"), 10);
                await queued.ForwardAsync(CreateSegment("session-b"), 42);

                var drainA = await queued.DrainSessionAsync("session-a", new CancellationTokenSource(TimeSpan.FromSeconds(5)).Token);
                var drainB = await queued.DrainSessionAsync("session-b", new CancellationTokenSource(TimeSpan.FromSeconds(5)).Token);

                Assert.IsTrue(drainA.Drained);
                Assert.IsTrue(drainB.Drained);
                Assert.AreEqual(10L, drainA.LastFinalSequenceNo, "他セッションのsequenceと混同しない");
                Assert.AreEqual(42L, drainB.LastFinalSequenceNo);

                // session Aのdrain後もqueueはcompleteされず、他セッションの転送は続く。
                var afterDrain = await queued.ForwardAsync(CreateSegment("session-b", "drain後の発言"), 43);
                Assert.IsTrue(afterDrain.Queued, "1セッションのdrainがqueue全体をcompleteしない");
                var drainB2 = await queued.DrainSessionAsync("session-b", new CancellationTokenSource(TimeSpan.FromSeconds(5)).Token);
                Assert.AreEqual(43L, drainB2.LastFinalSequenceNo);
            }
            finally
            {
                await queued.StopAsync(CancellationToken.None);
            }
        }

        [TestMethod]
        public async Task DrainSessionAsync_SucceedsWithNullSequenceWhenNoFinalWasForwarded()
        {
            var handler = new ScriptedHandler(_ => new HttpResponseMessage(HttpStatusCode.Created));
            var queued = CreateStartedForwarder(handler, out _);
            try
            {
                // finalが1件も無い(partialのみの)セッション。
                await queued.ForwardAsync(CreateSegment("session-a", "話し途中"), 0, isFinal: false);

                var drain = await queued.DrainSessionAsync("session-a", new CancellationTokenSource(TimeSpan.FromSeconds(5)).Token);

                Assert.IsTrue(drain.Drained);
                Assert.IsNull(drain.LastFinalSequenceNo);

                // 一度もenqueueされていないセッションも正常にdrain完了する。
                var unknown = await queued.DrainSessionAsync("session-never-seen", CancellationToken.None);
                Assert.IsTrue(unknown.Drained);
                Assert.IsNull(unknown.LastFinalSequenceNo);
            }
            finally
            {
                await queued.StopAsync(CancellationToken.None);
            }
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

        private sealed class ScriptedHandler : HttpMessageHandler
        {
            private readonly Func<HttpRequestMessage, HttpResponseMessage> responder;

            public ScriptedHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
            {
                this.responder = responder;
            }

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                return Task.Run(() => responder(request), CancellationToken.None);
            }
        }
    }
}
