using EchoBot.Authentication;
using EchoBot.Meetings;
using EchoBot.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Graph.Models;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Net;
using System.Text.Json;

namespace EchoBot.Tests
{
    [TestClass]
    public class MeetingTenantTests
    {
        private const string TenantId = "11111111-1111-1111-1111-111111111111";
        private const string ApplicationId = "22222222-2222-2222-2222-222222222222";

        [TestMethod]
        public async Task Provider_KeepsPostedTenantIdForMeetUrl()
        {
            var provider = CreateProvider();

            var joinInfo = await provider.GetJoinInfoAsync(
                new JoinCallBody
                {
                    JoinUrl = "https://teams.microsoft.com/meet/1234567890123?p=passcode",
                    TenantId = TenantId,
                },
                CancellationToken.None);

            Assert.AreEqual(TenantId, joinInfo.TenantId);
            Assert.IsInstanceOfType(joinInfo.MeetingInfo, typeof(JoinMeetingIdMeetingInfo));
        }

        [TestMethod]
        public void Validator_DistinguishesTenantIdFromApplicationId()
        {
            var normalizedTenantId = MeetingTenantValidator.NormalizeAndValidate(TenantId, ApplicationId);

            Assert.AreEqual(TenantId, normalizedTenantId);
            Assert.AreNotEqual(ApplicationId, normalizedTenantId);
        }

        [TestMethod]
        public void Validator_RejectsTenantIdThatEqualsApplicationId()
        {
            var ex = Assert.ThrowsException<TeamsMeetingJoinException>(() =>
                MeetingTenantValidator.NormalizeAndValidate(ApplicationId, ApplicationId));

            Assert.AreEqual("invalid_tenant_id", ex.Code);
            StringAssert.Contains(ex.Message, "not the application");
        }

        [TestMethod]
        [DataRow(null)]
        [DataRow("")]
        [DataRow("   ")]
        public void Validator_RejectsMissingTenantId(string? tenantId)
        {
            var ex = Assert.ThrowsException<TeamsMeetingJoinException>(() =>
                MeetingTenantValidator.NormalizeAndValidate(tenantId, ApplicationId));

            Assert.AreEqual("missing_tenant_id", ex.Code);
        }

        [TestMethod]
        public void Validator_RejectsInvalidGuidTenantId()
        {
            var ex = Assert.ThrowsException<TeamsMeetingJoinException>(() =>
                MeetingTenantValidator.NormalizeAndValidate("not-a-guid", ApplicationId));

            Assert.AreEqual("invalid_tenant_id", ex.Code);
        }

        [TestMethod]
        public void AuthenticationProvider_UsesMeetingTenantInsteadOfApplicationId()
        {
            var selectedTenant = AuthenticationProvider.SelectAuthenticationTenant(
                sdkTenant: ApplicationId,
                meetingTenant: TenantId,
                applicationId: ApplicationId);

            Assert.AreEqual(TenantId, selectedTenant);
        }

        [TestMethod]
        public void AuthenticationProvider_UsesSdkTenantWhenNoMeetingTenantExists()
        {
            var selectedTenant = AuthenticationProvider.SelectAuthenticationTenant(
                sdkTenant: TenantId,
                meetingTenant: null,
                applicationId: ApplicationId);

            Assert.AreEqual(TenantId, selectedTenant);
        }

        [TestMethod]
        public void AuthenticationProvider_RejectsApplicationIdAsTenant()
        {
            Assert.ThrowsException<InvalidOperationException>(() =>
                AuthenticationProvider.SelectAuthenticationTenant(
                    sdkTenant: ApplicationId,
                    meetingTenant: null,
                    applicationId: ApplicationId));
        }

        [TestMethod]
        public void AuthenticationProvider_BuildsAuthorityFromSelectedTenant()
        {
            var authority = AuthenticationProvider.BuildAuthority(TenantId);

            Assert.AreEqual($"https://login.microsoftonline.com/{TenantId}", authority);
            StringAssert.DoesNotMatch(authority, new System.Text.RegularExpressions.Regex(ApplicationId));
        }

        [TestMethod]
        public async Task Provider_KeepsMeetupJoinTenantId()
        {
            var provider = CreateProvider();

            var joinInfo = await provider.GetJoinInfoAsync(
                new JoinCallBody { JoinUrl = BuildMeetupJoinUrl(TenantId) },
                CancellationToken.None);

            Assert.AreEqual(TenantId, joinInfo.TenantId);
            Assert.IsInstanceOfType(joinInfo.MeetingInfo, typeof(OrganizerMeetingInfo));
        }

        private static TeamsMeetingJoinInfoProvider CreateProvider()
        {
            return new TeamsMeetingJoinInfoProvider(
                new TeamsMeetingUrlResolver(new HttpMessageInvoker(new NoNetworkHandler()), disposeClient: true),
                MeetingJoinOptions.FromValues(null),
                NullLogger<TeamsMeetingJoinInfoProvider>.Instance);
        }

        private static string BuildMeetupJoinUrl(string tenantId)
        {
            var context = WebUtility.UrlEncode(JsonSerializer.Serialize(new
            {
                Tid = tenantId,
                Oid = "organizer-id",
                MessageId = "reply-message-id",
            }));

            return "https://teams.microsoft.com/l/meetup-join/19:meeting_thread@thread.v2/0?context=" + context;
        }

        private sealed class NoNetworkHandler : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                Assert.Fail("Teams URLs should not require redirect resolution network calls.");
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError));
            }
        }
    }
}
