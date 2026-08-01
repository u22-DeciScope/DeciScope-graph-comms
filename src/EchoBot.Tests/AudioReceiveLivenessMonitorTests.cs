using EchoBot.Bot;
using EchoBot.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EchoBot.Tests
{
    [TestClass]
    public class AudioReceiveLivenessMonitorTests
    {
        private static readonly TimeSpan TestThreshold = TimeSpan.FromHours(1);

        [TestMethod]
        public void DeadlineBeforeFirstFrame_DoesNotReportAStall()
        {
            var updates = new List<BotMediaHealthUpdate>();
            using var monitor = CreateMonitor(updates);

            monitor.CheckDeadline(DateTimeOffset.Parse("2026-08-01T00:00:00Z"));

            Assert.AreEqual(0, updates.Count);
        }

        [TestMethod]
        public void SilentFrames_CountAsTransportActivity()
        {
            var updates = new List<BotMediaHealthUpdate>();
            using var monitor = CreateMonitor(updates);
            var first = DateTimeOffset.Parse("2026-08-01T00:00:00Z");

            monitor.ObserveFrame(first);
            monitor.ObserveFrame(first.AddMinutes(59));
            monitor.CheckDeadline(first.AddMinutes(60));

            Assert.AreEqual(0, updates.Count);
        }

        [TestMethod]
        public void StallAndRecovery_AreReportedExactlyOnceWithoutChangingCallState()
        {
            var updates = new List<BotMediaHealthUpdate>();
            using var monitor = CreateMonitor(updates);
            var first = DateTimeOffset.Parse("2026-08-01T00:00:00Z");

            monitor.ObserveFrame(first);
            monitor.CheckDeadline(first.Add(TestThreshold));
            monitor.CheckDeadline(first.Add(TestThreshold).AddMinutes(1));
            monitor.ObserveFrame(first.Add(TestThreshold).AddMinutes(2));
            monitor.ObserveFrame(first.Add(TestThreshold).AddMinutes(3));

            Assert.AreEqual(2, updates.Count);
            Assert.AreEqual("audio_receive_stalled", updates[0].State);
            Assert.AreEqual("started", updates[0].Event);
            Assert.AreEqual("ok", updates[1].State);
            Assert.AreEqual("recovered", updates[1].Event);
            Assert.AreEqual(updates[0].EventId.Replace(":started", string.Empty), updates[1].EventId.Replace(":recovered", string.Empty));
            Assert.AreEqual((long)TestThreshold.Add(TimeSpan.FromMinutes(2)).TotalMilliseconds, updates[1].DurationMs);
        }

        [TestMethod]
        public async Task Recovery_WaitsForStartedForwardingWithoutBlockingFrameObservation()
        {
            var updates = new List<BotMediaHealthUpdate>();
            var startedForwarding = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var releaseStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var monitor = new AudioReceiveLivenessMonitor(
                "call-1",
                async update =>
                {
                    updates.Add(update);
                    if (update.Event == "started")
                    {
                        startedForwarding.SetResult(true);
                        await releaseStarted.Task;
                    }
                },
                TestThreshold);
            var first = DateTimeOffset.Parse("2026-08-01T00:00:00Z");

            monitor.ObserveFrame(first);
            monitor.CheckDeadline(first.Add(TestThreshold));
            await startedForwarding.Task;
            monitor.ObserveFrame(first.Add(TestThreshold).AddSeconds(1));

            Assert.AreEqual(1, updates.Count, "recovery must queue behind the in-flight started event");
            releaseStarted.SetResult(true);
            await monitor.WaitForPendingPublicationsAsync();

            CollectionAssert.AreEqual(new[] { "started", "recovered" }, updates.Select(update => update.Event).ToArray());
        }

        [TestMethod]
        public void Dispose_DoesNotInventARecoveryOrStartEvent()
        {
            var updates = new List<BotMediaHealthUpdate>();
            var monitor = CreateMonitor(updates);
            var first = DateTimeOffset.Parse("2026-08-01T00:00:00Z");
            monitor.ObserveFrame(first);

            monitor.Dispose();
            monitor.CheckDeadline(first.Add(TestThreshold));
            monitor.ObserveFrame(first.Add(TestThreshold).AddMinutes(1));

            Assert.AreEqual(0, updates.Count);
        }

        private static AudioReceiveLivenessMonitor CreateMonitor(List<BotMediaHealthUpdate> updates)
        {
            return new AudioReceiveLivenessMonitor(
                "call-1",
                update =>
                {
                    updates.Add(update);
                    return Task.CompletedTask;
                },
                TestThreshold);
        }
    }
}
