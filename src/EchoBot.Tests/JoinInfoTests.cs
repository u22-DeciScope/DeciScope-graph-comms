using EchoBot.Models;
using Microsoft.Graph.Contracts;
using Microsoft.Graph.Models;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Net;
using System.Text.Json;

namespace EchoBot.Tests
{
    [TestClass]
    public class JoinInfoTests
    {
        private const string TenantId = "11111111-1111-1111-1111-111111111111";

        [TestMethod]
        public void ParseJoinURL_ParsesMeetupJoinUrl()
        {
            var url = BuildMeetupJoinUrl();

            var (chatInfo, meetingInfo) = JoinInfo.ParseJoinURL(url);
            var organizerInfo = (OrganizerMeetingInfo)meetingInfo;

            Assert.AreEqual("19:meeting_thread@thread.v2", chatInfo.ThreadId);
            Assert.AreEqual("0", chatInfo.MessageId);
            Assert.AreEqual("reply-message-id", chatInfo.ReplyChainMessageId);
            Assert.IsNotNull(organizerInfo.Organizer);
            Assert.IsNotNull(organizerInfo.Organizer.User);
            Assert.AreEqual("organizer-id", organizerInfo.Organizer.User.Id);
            Assert.AreEqual(TenantId, organizerInfo.Organizer.GetPrimaryIdentity().GetTenantId());
        }

        [TestMethod]
        public void ParseJoinURL_ParsesMeetUrl()
        {
            var (chatInfo, meetingInfo) = JoinInfo.ParseJoinURL("https://teams.microsoft.com/meet/1234567890123?p=abcDEF&anon=true");
            var meetingIdInfo = (JoinMeetingIdMeetingInfo)meetingInfo;

            Assert.IsNull(chatInfo.ThreadId);
            Assert.AreEqual("1234567890123", meetingIdInfo.JoinMeetingId);
            Assert.AreEqual("abcDEF", meetingIdInfo.Passcode);
        }

        [TestMethod]
        public void ParseJoinURL_DecodesEncodedValuesAndAdditionalQueryParameters()
        {
            var context = WebUtility.UrlEncode(JsonSerializer.Serialize(new
            {
                Tid = TenantId,
                Oid = "organizer-id",
                MessageId = "reply-message-id",
            }));
            var url = "https://teams.microsoft.com/l/meetup-join/19%3ameeting_thread%40thread.v2/0?foo=bar&context=" + context;

            var (chatInfo, meetingInfo) = JoinInfo.ParseJoinURL(url);
            var organizerInfo = (OrganizerMeetingInfo)meetingInfo;

            Assert.AreEqual("19:meeting_thread@thread.v2", chatInfo.ThreadId);
            Assert.AreEqual(TenantId, organizerInfo.Organizer.GetPrimaryIdentity().GetTenantId());
        }

        [TestMethod]
        [DataRow(null)]
        [DataRow("")]
        [DataRow("   ")]
        [DataRow("not a url")]
        public void ParseJoinURL_RejectsInvalidInput(string? url)
        {
            Assert.ThrowsException<ArgumentException>(() => JoinInfo.ParseJoinURL(url!));
        }

        [TestMethod]
        public void ParseJoinURL_RejectsUrlWithMissingContext()
        {
            Assert.ThrowsException<ArgumentException>(() =>
                JoinInfo.ParseJoinURL("https://teams.microsoft.com/l/meetup-join/19:meeting_thread@thread.v2/0"));
        }

        [TestMethod]
        public void ParseJoinURL_RejectsMeetUrlWithoutPasscode()
        {
            Assert.ThrowsException<ArgumentException>(() =>
                JoinInfo.ParseJoinURL("https://teams.microsoft.com/meet/1234567890123"));
        }

        private static string BuildMeetupJoinUrl()
        {
            var context = WebUtility.UrlEncode(JsonSerializer.Serialize(new
            {
                Tid = TenantId,
                Oid = "organizer-id",
                MessageId = "reply-message-id",
            }));

            return "https://teams.microsoft.com/l/meetup-join/19:meeting_thread@thread.v2/0?context=" + context;
        }
    }
}
