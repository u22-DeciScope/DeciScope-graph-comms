using EchoBot.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EchoBot.Tests
{
    [TestClass]
    public class BotMeetingStatusReporterTests
    {
        [TestMethod]
        public void BuildStatusUrl_ReusesTranscriptApiBase()
        {
            var url = BotMeetingStatusReporter.BuildStatusUrl(
                new Uri("http://go-api:8080/api/v1/transcript-segments"),
                "session 1");

            Assert.AreEqual("http://go-api:8080/api/v1/bot/meeting-sessions/session%201/status", url.AbsoluteUri);
        }

        [TestMethod]
        public void BuildStatusUrl_DropsTranscriptQuery()
        {
            var url = BotMeetingStatusReporter.BuildStatusUrl(
                new Uri("https://example.test/prefix/api/v1/transcript-segments?x=1"),
                "session_1");

            Assert.AreEqual("https://example.test/prefix/api/v1/bot/meeting-sessions/session_1/status", url.AbsoluteUri);
        }
    }
}
