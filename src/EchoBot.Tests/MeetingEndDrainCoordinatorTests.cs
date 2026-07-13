using EchoBot.Models;
using EchoBot.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EchoBot.Tests
{
    [TestClass]
    public class MeetingEndDrainCoordinatorTests
    {
        private static TranscriptForwardingOptions OptionsWithTimeouts(
            string speechStopSeconds = "5",
            string callbackSeconds = "5",
            string transcriptSeconds = "5")
        {
            return TranscriptForwardingOptions.FromValues(
                "true",
                "http://localhost/api/v1/transcript-segments",
                "secret-key",
                "5",
                speechStopTimeoutSecondsValue: speechStopSeconds,
                callbackDrainTimeoutSecondsValue: callbackSeconds,
                transcriptDrainTimeoutSecondsValue: transcriptSeconds);
        }

        [TestMethod]
        public async Task DrainAsync_RunsStagesInOrderAndReportsDrainedResult()
        {
            var speech = new FakeSpeechShutdown();
            var forwarder = new FakeForwarder(
                drain: _ => Task.FromResult(new TranscriptDrainResult(true, 27, 0, 0)),
                order: speech.Order);
            var coordinator = new MeetingEndDrainCoordinator(forwarder, OptionsWithTimeouts(), NullLogger.Instance);

            var outcome = await coordinator.DrainAsync("session-1", "call-1", "manual_end_requested", speech);

            Assert.IsTrue(outcome.SpeechStopped);
            Assert.IsTrue(outcome.CallbacksDrained);
            Assert.IsTrue(outcome.TranscriptQueueDrained);
            Assert.AreEqual(27L, outcome.LastFinalSequenceNo);
            Assert.IsNull(outcome.TimeoutReason);
            CollectionAssert.AreEqual(
                new[] { "stop", "callbacks", "drain" },
                speech.Order);
        }

        [TestMethod]
        public async Task DrainAsync_WaitsForRunningCallbackBeforeQueueDrain()
        {
            // callback完了待ちが実際にブロックし、完了後にqueue drainへ進むこと。
            var speech = new FakeSpeechShutdown { CallbackHoldMs = 300 };
            var forwarder = new FakeForwarder(
                drain: _ => Task.FromResult(new TranscriptDrainResult(true, 12, 0, 0)),
                order: speech.Order);
            var coordinator = new MeetingEndDrainCoordinator(forwarder, OptionsWithTimeouts(), NullLogger.Instance);

            var outcome = await coordinator.DrainAsync("session-1", "call-1", "manual_end_requested", speech);

            Assert.IsTrue(outcome.CallbacksDrained);
            Assert.IsTrue(speech.CallbackWaitCompletedBeforeDrain);
            Assert.AreEqual(12L, outcome.LastFinalSequenceNo);
        }

        [TestMethod]
        public async Task DrainAsync_ReportsNotDrainedOnCallbackTimeoutWithoutLying()
        {
            var speech = new FakeSpeechShutdown { CallbacksNeverFinish = true, Pending = 1 };
            var forwarder = new FakeForwarder(
                drain: _ => Task.FromResult(new TranscriptDrainResult(true, 24, 0, 0)),
                order: speech.Order);
            var coordinator = new MeetingEndDrainCoordinator(
                forwarder, OptionsWithTimeouts(callbackSeconds: "1"), NullLogger.Instance);

            var outcome = await coordinator.DrainAsync("session-1", "call-1", "manual_end_requested", speech);

            Assert.IsFalse(outcome.CallbacksDrained);
            Assert.IsFalse(outcome.TranscriptQueueDrained, "timeout時にdrained=trueと偽ってはいけない");
            Assert.AreEqual("callback_drain_timeout", outcome.TimeoutReason);
            // 転送成功済みの最大sequenceはそのまま通知する。
            Assert.AreEqual(24L, outcome.LastFinalSequenceNo);
        }

        [TestMethod]
        public async Task DrainAsync_ReportsTimeoutReasonWhenQueueDrainTimesOut()
        {
            var speech = new FakeSpeechShutdown();
            var forwarder = new FakeForwarder(
                drain: _ => Task.FromResult(new TranscriptDrainResult(false, 24, 2, 0)),
                order: speech.Order);
            var coordinator = new MeetingEndDrainCoordinator(forwarder, OptionsWithTimeouts(), NullLogger.Instance);

            var outcome = await coordinator.DrainAsync("session-1", "call-1", "manual_end_requested", speech);

            Assert.IsFalse(outcome.TranscriptQueueDrained);
            Assert.AreEqual("transcript_drain_timeout", outcome.TimeoutReason);
            Assert.AreEqual(24L, outcome.LastFinalSequenceNo);
            Assert.AreEqual(2, outcome.TranscriptDrain.PendingCount);
        }

        [TestMethod]
        public async Task DrainAsync_DoesNotHangWhenSpeechStopNeverCompletes()
        {
            var speech = new FakeSpeechShutdown { SpeechStopNeverFinishes = true };
            var forwarder = new FakeForwarder(
                drain: _ => Task.FromResult(new TranscriptDrainResult(true, null, 0, 0)),
                order: speech.Order);
            var coordinator = new MeetingEndDrainCoordinator(
                forwarder, OptionsWithTimeouts(speechStopSeconds: "1"), NullLogger.Instance);

            var outcome = await coordinator.DrainAsync("session-1", "call-1", "manual_end_requested", speech)
                .WaitAsync(TimeSpan.FromSeconds(10));

            Assert.IsFalse(outcome.SpeechStopped);
            Assert.IsFalse(outcome.TranscriptQueueDrained);
            Assert.AreEqual("speech_stop_timeout", outcome.TimeoutReason);
        }

        [TestMethod]
        public async Task DrainAsync_CoalescesConcurrentCallsIntoSingleExecution()
        {
            var speech = new FakeSpeechShutdown { CallbackHoldMs = 200 };
            var drainCalls = 0;
            var forwarder = new FakeForwarder(
                drain: _ =>
                {
                    Interlocked.Increment(ref drainCalls);
                    return Task.FromResult(new TranscriptDrainResult(true, 5, 0, 0));
                },
                order: speech.Order);
            var coordinator = new MeetingEndDrainCoordinator(forwarder, OptionsWithTimeouts(), NullLogger.Instance);

            var first = coordinator.DrainAsync("session-1", "call-1", "manual_end_requested", speech);
            var second = coordinator.DrainAsync("session-1", "call-1", "graph_terminated", speech);

            var outcomes = await Task.WhenAll(first, second);

            Assert.AreSame(first, second, "二重終了は同じdrain Taskへ集約される");
            Assert.AreEqual(1, drainCalls);
            Assert.AreEqual(1, speech.StopCalls);
            Assert.AreEqual(outcomes[0], outcomes[1]);
        }

        private sealed class FakeSpeechShutdown : ISpeechTranscriptionShutdown
        {
            public List<string> Order { get; } = new List<string>();

            public int Pending { get; set; }

            public int CallbackHoldMs { get; set; }

            public bool CallbacksNeverFinish { get; set; }

            public bool SpeechStopNeverFinishes { get; set; }

            public bool CallbackWaitCompletedBeforeDrain { get; private set; }

            public int StopCalls;

            public int PendingRecognitionCallbackCount => Pending;

            public async Task StopSpeechTranscriptionAsync(CancellationToken cancellationToken = default)
            {
                Interlocked.Increment(ref StopCalls);
                lock (Order)
                {
                    Order.Add("stop");
                }

                if (SpeechStopNeverFinishes)
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, CancellationToken.None);
                }
            }

            public async Task<bool> WaitForRecognitionCallbacksAsync(CancellationToken cancellationToken = default)
            {
                lock (Order)
                {
                    Order.Add("callbacks");
                }

                if (CallbacksNeverFinish)
                {
                    try
                    {
                        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                    }
                    catch (OperationCanceledException)
                    {
                    }

                    return false;
                }

                if (CallbackHoldMs > 0)
                {
                    await Task.Delay(CallbackHoldMs, CancellationToken.None);
                }

                CallbackWaitCompletedBeforeDrain = true;
                return true;
            }
        }

        private sealed class FakeForwarder : ITranscriptForwarder
        {
            private readonly Func<string?, Task<TranscriptDrainResult>> drain;
            private readonly List<string> order;

            public FakeForwarder(Func<string?, Task<TranscriptDrainResult>> drain, List<string> order)
            {
                this.drain = drain;
                this.order = order;
            }

            public Task<TranscriptForwardResult> ForwardAsync(
                TranscriptSegment segment,
                int sequenceNo,
                bool isFinal = true,
                CancellationToken cancellationToken = default)
            {
                return Task.FromResult(TranscriptForwardResult.QueuedForDelivery());
            }

            public Task<TranscriptDrainResult> DrainSessionAsync(
                string? sessionId,
                CancellationToken cancellationToken = default)
            {
                lock (order)
                {
                    order.Add("drain");
                }

                return drain(sessionId);
            }
        }
    }
}
