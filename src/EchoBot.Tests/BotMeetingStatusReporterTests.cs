using EchoBot.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Text.Json;

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

        [TestMethod]
        public void BuildHeartbeatUrl_ReusesTranscriptApiBase()
        {
            var url = BotMeetingStatusReporter.BuildHeartbeatUrl(
                new Uri("http://go-api:8080/api/v1/transcript-segments"),
                "session 1");

            Assert.AreEqual("http://go-api:8080/api/v1/bot/meeting-sessions/session%201/heartbeat", url.AbsoluteUri);
        }

        [TestMethod]
        public void BuildHeartbeatUrl_WithoutApiV1Segment_AppendsDefaultPath()
        {
            var url = BotMeetingStatusReporter.BuildHeartbeatUrl(
                new Uri("http://go-api:8080/transcript-segments"),
                "session_1");

            Assert.AreEqual("http://go-api:8080/api/v1/bot/meeting-sessions/session_1/heartbeat", url.AbsoluteUri);
        }

        [TestMethod]
        public void BuildHeartbeatUrl_DropsTranscriptQuery()
        {
            var url = BotMeetingStatusReporter.BuildHeartbeatUrl(
                new Uri("https://example.test/prefix/api/v1/transcript-segments?x=1"),
                "session_1");

            Assert.AreEqual("https://example.test/prefix/api/v1/bot/meeting-sessions/session_1/heartbeat", url.AbsoluteUri);
        }

        [TestMethod]
        public void BuildHeartbeatUrl_EscapesSessionId()
        {
            var url = BotMeetingStatusReporter.BuildHeartbeatUrl(
                new Uri("http://go-api:8080/api/v1/transcript-segments"),
                "session/with slash");

            StringAssert.Contains(url.AbsoluteUri, "session%2Fwith%20slash");
        }

        [TestMethod]
        public void StatusUpdate_SerializesFailureDiagnostics()
        {
            var update = new BotMeetingStatusUpdate(
                BotMeetingStatus.Failed,
                "call-1",
                "failed to join meeting",
                failedReason: "graph_join_failed",
                errorCode: "ServiceException",
                source: "graph_join");

            var json = JsonSerializer.Serialize(update, new JsonSerializerOptions(JsonSerializerDefaults.Web));

            StringAssert.Contains(json, "\"status\":\"failed\"");
            StringAssert.Contains(json, "\"botCallId\":\"call-1\"");
            StringAssert.Contains(json, "\"failedReason\":\"graph_join_failed\"");
            StringAssert.Contains(json, "\"errorCode\":\"ServiceException\"");
            StringAssert.Contains(json, "\"source\":\"graph_join\"");
        }
    }
}
