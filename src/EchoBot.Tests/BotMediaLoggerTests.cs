using EchoBot.Bot;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using MediaLogLevel = Microsoft.Skype.Bots.Media.LogLevel;

namespace EchoBot.Tests
{
    [TestClass]
    public class BotMediaLoggerTests
    {
        [TestMethod]
        public void ShouldSuppress_SuppressesLowOnFramesInformationLog()
        {
            var suppress = BotMediaLogger.ShouldSuppress(
                MediaLogLevel.Information,
                "[SkypeBotsMediaPlatform] the audio player low on frames event was raised with 260 remaining ms");

            Assert.IsTrue(suppress);
        }

        [TestMethod]
        public void ShouldSuppress_DoesNotSuppressWarnings()
        {
            var suppress = BotMediaLogger.ShouldSuppress(
                MediaLogLevel.Warning,
                "[SkypeBotsMediaPlatform] the audio player low on frames event was raised with 260 remaining ms");

            Assert.IsFalse(suppress);
        }

        [TestMethod]
        public void ShouldSuppress_DoesNotSuppressOtherInformationLogs()
        {
            var suppress = BotMediaLogger.ShouldSuppress(
                MediaLogLevel.Information,
                "[SkypeBotsMediaPlatform] AudioSocket disposed");

            Assert.IsFalse(suppress);
        }
    }
}
