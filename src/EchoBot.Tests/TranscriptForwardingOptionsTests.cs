using EchoBot.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EchoBot.Tests
{
    [TestClass]
    public class TranscriptForwardingOptionsTests
    {
        [TestMethod]
        public void HeartbeatSeconds_Unset_DefaultsToTwenty()
        {
            var options = TranscriptForwardingOptions.FromValues(
                "true", "http://localhost/api/v1/transcript-segments", "secret", "5");

            Assert.AreEqual(20, options.HeartbeatSeconds);
        }

        [TestMethod]
        public void HeartbeatSeconds_Zero_DisablesHeartbeat()
        {
            var options = TranscriptForwardingOptions.FromValues(
                "true", "http://localhost/api/v1/transcript-segments", "secret", "5",
                heartbeatSecondsValue: "0");

            Assert.AreEqual(0, options.HeartbeatSeconds);
        }

        [TestMethod]
        public void HeartbeatSeconds_Negative_DisablesHeartbeat()
        {
            var options = TranscriptForwardingOptions.FromValues(
                "true", "http://localhost/api/v1/transcript-segments", "secret", "5",
                heartbeatSecondsValue: "-5");

            Assert.AreEqual(0, options.HeartbeatSeconds);
        }

        [TestMethod]
        public void HeartbeatSeconds_Unparseable_DefaultsToTwenty()
        {
            var options = TranscriptForwardingOptions.FromValues(
                "true", "http://localhost/api/v1/transcript-segments", "secret", "5",
                heartbeatSecondsValue: "abc");

            Assert.AreEqual(20, options.HeartbeatSeconds);
        }

        [TestMethod]
        public void HeartbeatSeconds_ValidValue_UsesValue()
        {
            var options = TranscriptForwardingOptions.FromValues(
                "true", "http://localhost/api/v1/transcript-segments", "secret", "5",
                heartbeatSecondsValue: "45");

            Assert.AreEqual(45, options.HeartbeatSeconds);
        }
    }
}
