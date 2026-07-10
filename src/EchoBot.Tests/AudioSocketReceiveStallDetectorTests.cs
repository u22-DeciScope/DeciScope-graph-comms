using EchoBot.Bot;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EchoBot.Tests
{
    [TestClass]
    public class AudioSocketReceiveStallDetectorTests
    {
        [TestMethod]
        public void IsReceiveStallLog_MatchesReceiveDirectionStallMessage()
        {
            Assert.IsTrue(AudioSocketReceiveStallDetector.IsReceiveStallLog(
                "[SkypeBotsMediaPlatform] Detected a stall on Audio socket 12345, direction: Receive"));
        }

        [TestMethod]
        public void IsReceiveStallLog_DoesNotMatchSendDirection()
        {
            Assert.IsFalse(AudioSocketReceiveStallDetector.IsReceiveStallLog(
                "[SkypeBotsMediaPlatform] Detected a stall on Audio socket 12345, direction: Send"));
        }

        [TestMethod]
        public void IsReceiveStallLog_DoesNotMatchUnrelatedLog()
        {
            Assert.IsFalse(AudioSocketReceiveStallDetector.IsReceiveStallLog(
                "[SkypeBotsMediaPlatform] AudioSocket disposed"));
        }

        [TestMethod]
        public void IsReceiveStallLog_HandlesNullAndEmpty()
        {
            Assert.IsFalse(AudioSocketReceiveStallDetector.IsReceiveStallLog(null));
            Assert.IsFalse(AudioSocketReceiveStallDetector.IsReceiveStallLog(string.Empty));
        }

        [TestMethod]
        public void RecordFromLog_MatchingLog_UpdatesCountAndTimestamp()
        {
            var detector = new AudioSocketReceiveStallDetector();
            Assert.IsNull(detector.LastReceiveStallAtUtc);
            Assert.AreEqual(0, detector.ReceiveStallCount);

            var before = DateTimeOffset.UtcNow;
            detector.RecordFromLog("Detected a stall on Audio socket 1, direction: Receive");
            var after = DateTimeOffset.UtcNow;

            Assert.AreEqual(1, detector.ReceiveStallCount);
            Assert.IsNotNull(detector.LastReceiveStallAtUtc);
            Assert.IsTrue(detector.LastReceiveStallAtUtc >= before && detector.LastReceiveStallAtUtc <= after);
        }

        [TestMethod]
        public void RecordFromLog_NonMatchingLog_DoesNotUpdate()
        {
            var detector = new AudioSocketReceiveStallDetector();
            detector.RecordFromLog("Detected a stall on Audio socket 1, direction: Send");

            Assert.AreEqual(0, detector.ReceiveStallCount);
            Assert.IsNull(detector.LastReceiveStallAtUtc);
        }

        [TestMethod]
        public void RecordFromLog_MultipleMatches_IncrementsCount()
        {
            var detector = new AudioSocketReceiveStallDetector();
            detector.RecordFromLog("Detected a stall on Audio socket 1, direction: Receive");
            detector.RecordFromLog("Detected a stall on Audio socket 2, direction: Receive");

            Assert.AreEqual(2, detector.ReceiveStallCount);
        }
    }
}
