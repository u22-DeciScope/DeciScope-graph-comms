using EchoBot.Bot;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EchoBot.Tests
{
    [TestClass]
    public class BotMediaMetricsCalculatorTests
    {
        [TestMethod]
        public void FramesSinceLastNonZeroAudio_WhenNeverObserved_ReturnsReceivedFrames()
        {
            var frames = BotMediaMetricsCalculator.FramesSinceLastNonZeroAudio(100, 0, hasObservedNonZeroAudio: false);
            Assert.AreEqual(100, frames);
        }

        [TestMethod]
        public void FramesSinceLastNonZeroAudio_WhenObserved_ReturnsDelta()
        {
            var frames = BotMediaMetricsCalculator.FramesSinceLastNonZeroAudio(100, 80, hasObservedNonZeroAudio: true);
            Assert.AreEqual(20, frames);
        }

        [TestMethod]
        public void FramesSinceLastNonZeroAudio_NeverNegative()
        {
            var frames = BotMediaMetricsCalculator.FramesSinceLastNonZeroAudio(50, 80, hasObservedNonZeroAudio: true);
            Assert.AreEqual(0, frames);
        }

        [TestMethod]
        public void SecondsSinceLastNonZeroAudio_WhenNeverObserved_ReturnsZero()
        {
            var seconds = BotMediaMetricsCalculator.SecondsSinceLastNonZeroAudio(DateTimeOffset.UtcNow, null);
            Assert.AreEqual(0, seconds);
        }

        [TestMethod]
        public void SecondsSinceLastNonZeroAudio_ComputesElapsedSeconds()
        {
            var now = DateTimeOffset.UtcNow;
            var lastNonZero = now.AddSeconds(-45);

            var seconds = BotMediaMetricsCalculator.SecondsSinceLastNonZeroAudio(now, lastNonZero);

            Assert.AreEqual(45, seconds);
        }

        [TestMethod]
        public void SecondsSinceLastNonZeroAudio_NeverNegative()
        {
            var now = DateTimeOffset.UtcNow;
            var future = now.AddSeconds(5);

            var seconds = BotMediaMetricsCalculator.SecondsSinceLastNonZeroAudio(now, future);

            Assert.AreEqual(0, seconds);
        }

        [TestMethod]
        public void IsAudioStalled_WhenNoStallRecorded_ReturnsFalse()
        {
            Assert.IsFalse(BotMediaMetricsCalculator.IsAudioStalled(DateTimeOffset.UtcNow, null, TimeSpan.FromSeconds(30)));
        }

        [TestMethod]
        public void IsAudioStalled_WhenStallWithinWindow_ReturnsTrue()
        {
            var now = DateTimeOffset.UtcNow;
            var lastStall = now.AddSeconds(-10);

            Assert.IsTrue(BotMediaMetricsCalculator.IsAudioStalled(now, lastStall, TimeSpan.FromSeconds(30)));
        }

        [TestMethod]
        public void IsAudioStalled_WhenStallOutsideWindow_ReturnsFalse()
        {
            var now = DateTimeOffset.UtcNow;
            var lastStall = now.AddSeconds(-45);

            Assert.IsFalse(BotMediaMetricsCalculator.IsAudioStalled(now, lastStall, TimeSpan.FromSeconds(30)));
        }

        [TestMethod]
        public void Latest_ReturnsLaterTimestamp()
        {
            var earlier = DateTimeOffset.UtcNow.AddSeconds(-10);
            var later = DateTimeOffset.UtcNow;

            Assert.AreEqual(later, BotMediaMetricsCalculator.Latest(earlier, later));
            Assert.AreEqual(later, BotMediaMetricsCalculator.Latest(later, earlier));
        }

        [TestMethod]
        public void Latest_HandlesNulls()
        {
            var value = DateTimeOffset.UtcNow;

            Assert.AreEqual(value, BotMediaMetricsCalculator.Latest(null, value));
            Assert.AreEqual(value, BotMediaMetricsCalculator.Latest(value, null));
            Assert.IsNull(BotMediaMetricsCalculator.Latest(null, null));
        }
    }
}
