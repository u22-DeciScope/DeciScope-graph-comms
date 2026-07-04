using EchoBot.Media;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EchoBot.Tests
{
    [TestClass]
    public class SpeechServiceTests
    {
        [TestMethod]
        public void ShouldLogDroppedFrame_LogsFirstAndIntervalOnly()
        {
            Assert.IsTrue(SpeechService.ShouldLogDroppedFrame(1));
            Assert.IsFalse(SpeechService.ShouldLogDroppedFrame(2));
            Assert.IsFalse(SpeechService.ShouldLogDroppedFrame(249));
            Assert.IsTrue(SpeechService.ShouldLogDroppedFrame(250));
            Assert.IsFalse(SpeechService.ShouldLogDroppedFrame(251));
            Assert.IsTrue(SpeechService.ShouldLogDroppedFrame(500));
        }

        [TestMethod]
        public void IsThrottleCancellation_DetectsTooManyRequestsAnd4429()
        {
            Assert.IsTrue(SpeechService.IsThrottleCancellation("TooManyRequests", null));
            Assert.IsTrue(SpeechService.IsThrottleCancellation("ServiceError", "ErrorCode=4429"));
            Assert.IsFalse(SpeechService.IsThrottleCancellation("ServiceError", "connection reset"));
        }

        [TestMethod]
        public void GetReconnectDelay_ThrottledUsesTwoSecondBase()
        {
            Assert.AreEqual(TimeSpan.FromSeconds(2), SpeechService.GetReconnectDelay(1, throttled: true));
        }

        [TestMethod]
        public void GetReconnectDelay_NonThrottledUsesOneSecondBase()
        {
            Assert.AreEqual(TimeSpan.FromSeconds(1), SpeechService.GetReconnectDelay(1, throttled: false));
        }

        [TestMethod]
        public void GetReconnectDelay_IsMonotonicallyIncreasingWithAttempt()
        {
            var previous = TimeSpan.Zero;
            for (var attempt = 1; attempt <= 8; attempt++)
            {
                var current = SpeechService.GetReconnectDelay(attempt, throttled: true);
                Assert.IsTrue(current >= previous, $"Expected delay to be non-decreasing at attempt {attempt}: previous={previous}, current={current}");
                previous = current;
            }
        }

        [TestMethod]
        public void GetReconnectDelay_CapsAtThirtySeconds_Throttled()
        {
            Assert.AreEqual(TimeSpan.FromSeconds(30), SpeechService.GetReconnectDelay(10, throttled: true));
            Assert.AreEqual(TimeSpan.FromSeconds(30), SpeechService.GetReconnectDelay(100, throttled: true));
        }

        [TestMethod]
        public void GetReconnectDelay_CapsAtThirtySeconds_NonThrottled()
        {
            Assert.AreEqual(TimeSpan.FromSeconds(30), SpeechService.GetReconnectDelay(10, throttled: false));
            Assert.AreEqual(TimeSpan.FromSeconds(30), SpeechService.GetReconnectDelay(100, throttled: false));
        }

        [TestMethod]
        public void GetReconnectDelay_DoublesUntilCapReached()
        {
            // Non-throttled base is 1s: attempt 1 -> 1s, attempt 2 -> 2s, attempt 3 -> 4s ...
            Assert.AreEqual(TimeSpan.FromSeconds(1), SpeechService.GetReconnectDelay(1, throttled: false));
            Assert.AreEqual(TimeSpan.FromSeconds(2), SpeechService.GetReconnectDelay(2, throttled: false));
            Assert.AreEqual(TimeSpan.FromSeconds(4), SpeechService.GetReconnectDelay(3, throttled: false));
            Assert.AreEqual(TimeSpan.FromSeconds(8), SpeechService.GetReconnectDelay(4, throttled: false));
            Assert.AreEqual(TimeSpan.FromSeconds(16), SpeechService.GetReconnectDelay(5, throttled: false));
            Assert.AreEqual(TimeSpan.FromSeconds(30), SpeechService.GetReconnectDelay(6, throttled: false));
        }
    }
}
