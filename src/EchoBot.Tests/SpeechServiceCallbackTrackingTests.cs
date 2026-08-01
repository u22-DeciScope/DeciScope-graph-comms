using EchoBot;
using EchoBot.Media;
using EchoBot.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EchoBot.Tests
{
    [TestClass]
    public class SpeechServiceCallbackTrackingTests
    {
        private static SpeechService CreateService()
        {
            var settings = new AppSettings
            {
                SpeechKey = "test-key",
                SpeechRegion = "japaneast",
                SpeechRecognitionLanguage = "ja-JP",
            };
            return new SpeechService(
                "call-1",
                settings,
                NullLogger.Instance,
                new InMemoryTranscriptSequenceProvider(),
                new NoopForwarder(),
                new NoopStatusReporter(),
                sessionId: "session-1");
        }

        [TestMethod]
        public async Task WaitForRecognitionCallbacksAsync_ReturnsImmediatelyWhenNothingPending()
        {
            await using var service = CreateService();

            var drained = await service.WaitForRecognitionCallbacksAsync(CancellationToken.None);

            Assert.IsTrue(drained);
            Assert.AreEqual(0, service.PendingRecognitionCallbacks);
        }

        [TestMethod]
        public async Task WaitForRecognitionCallbacksAsync_WaitsForRunningCallbackCompletion()
        {
            await using var service = CreateService();
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            service.TrackRecognitionCallback(() => release.Task);
            Assert.AreEqual(1, service.PendingRecognitionCallbacks);

            var waitTask = service.WaitForRecognitionCallbacksAsync(CancellationToken.None);
            Assert.IsFalse(waitTask.IsCompleted, "callback完了前にdrainが完了してはいけない");

            release.SetResult(true);
            var drained = await waitTask.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.IsTrue(drained);
            Assert.AreEqual(0, service.PendingRecognitionCallbacks);
        }

        [TestMethod]
        public async Task WaitForRecognitionCallbacksAsync_ReturnsFalseWhenCancelledWhilePending()
        {
            await using var service = CreateService();
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            service.TrackRecognitionCallback(() => release.Task);

            using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
            var drained = await service.WaitForRecognitionCallbacksAsync(cts.Token);

            Assert.IsFalse(drained, "timeout時はdrain未完了として返す");
            Assert.AreEqual(1, service.PendingRecognitionCallbacks, "timeout後も残callback数を追跡できる");

            release.SetResult(true);
            var drainedAfter = await service.WaitForRecognitionCallbacksAsync(CancellationToken.None)
                .WaitAsync(TimeSpan.FromSeconds(5));
            Assert.IsTrue(drainedAfter);
        }

        [TestMethod]
        public async Task TrackRecognitionCallback_ObservesCallbackExceptionAndStillCompletes()
        {
            await using var service = CreateService();

            service.TrackRecognitionCallback(() => Task.FromException(new InvalidOperationException("boom")));

            var drained = await service.WaitForRecognitionCallbacksAsync(CancellationToken.None)
                .WaitAsync(TimeSpan.FromSeconds(5));

            Assert.IsTrue(drained, "callback内例外でもdrainが完了する(未観測例外にならない)");
            Assert.AreEqual(0, service.PendingRecognitionCallbacks);
        }

        [TestMethod]
        public async Task WaitForRecognitionCallbacksAsync_HandlesMultipleConcurrentCallbacks()
        {
            await using var service = CreateService();
            var first = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var second = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            service.TrackRecognitionCallback(() => first.Task);
            service.TrackRecognitionCallback(() => second.Task);
            Assert.AreEqual(2, service.PendingRecognitionCallbacks);

            var waitTask = service.WaitForRecognitionCallbacksAsync(CancellationToken.None);
            first.SetResult(true);
            await Task.Delay(50);
            Assert.IsFalse(waitTask.IsCompleted, "全callbackが終わるまで完了しない");

            second.SetResult(true);
            Assert.IsTrue(await waitTask.WaitAsync(TimeSpan.FromSeconds(5)));
        }

        private sealed class NoopForwarder : ITranscriptForwarder
        {
            public Task<TranscriptForwardResult> ForwardAsync(
                Models.TranscriptSegment segment,
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
                return Task.FromResult(new TranscriptDrainResult(true, null, 0, 0));
            }
        }

        private sealed class NoopStatusReporter : IBotMeetingStatusReporter
        {
            public TimeSpan? HeartbeatInterval => null;

            public Task ReportAsync(
                string? sessionId,
                string status,
                string message,
                string? botCallId = null,
                CancellationToken cancellationToken = default,
                string? failedReason = null,
                string? errorCode = null,
                string? source = null,
                string? endReason = null,
                DateTimeOffset? endedAt = null,
                long? lastFinalSequenceNo = null,
                bool? transcriptQueueDrained = null)
            {
                return Task.CompletedTask;
            }

            public Task ReportMetadataAsync(
                string? sessionId,
                Services.BotMeetingMetadataUpdate metadata,
                CancellationToken cancellationToken = default)
            {
                return Task.CompletedTask;
            }

            public Task ReportHeartbeatAsync(
                string? sessionId,
                string? botCallId,
                CancellationToken cancellationToken = default,
                Bot.BotMediaMetricsSnapshot? metrics = null)
            {
                return Task.CompletedTask;
            }

            public Task ReportMediaHealthAsync(
                string? sessionId,
                string? botCallId,
                Services.BotMediaHealthUpdate update,
                CancellationToken cancellationToken = default)
            {
                return Task.CompletedTask;
            }
        }
    }
}
