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
    }
}
