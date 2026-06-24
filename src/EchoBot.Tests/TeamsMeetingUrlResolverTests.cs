using EchoBot.Meetings;
using EchoBot.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Graph.Models;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Net;

namespace EchoBot.Tests
{
    [TestClass]
    public class TeamsMeetingUrlResolverTests
    {
        private const string TenantId = "11111111-1111-1111-1111-111111111111";

        [TestMethod]
        public async Task ResolveAsync_ReturnsTeamsUrlWithoutNetwork()
        {
            var handler = new SequenceHandler();
            using var resolver = new TeamsMeetingUrlResolver(new HttpMessageInvoker(handler), disposeClient: true);

            var (resolvedUrl, redirected) = await resolver.ResolveAsync(
                "https://teams.microsoft.com/meet/123456789?p=passcode",
                CancellationToken.None);

            Assert.AreEqual("https://teams.microsoft.com/meet/123456789?p=passcode", resolvedUrl.ToString());
            Assert.IsFalse(redirected);
            Assert.AreEqual(0, handler.RequestCount);
        }

        [TestMethod]
        public async Task Provider_ParsesShortenedUrlAfterRedirect()
        {
            var handler = new SequenceHandler(
                Redirect("https://teams.microsoft.com/meet/123456789?p=passcode"));
            using var resolver = new TeamsMeetingUrlResolver(new HttpMessageInvoker(handler), disposeClient: true);
            var provider = new TeamsMeetingJoinInfoProvider(resolver, NullLogger<TeamsMeetingJoinInfoProvider>.Instance);

            var joinInfo = await provider.GetJoinInfoAsync(
                new JoinCallBody
                {
                    JoinUrl = "https://aka.ms/deci-join",
                    TenantId = TenantId,
                },
                CancellationToken.None);

            Assert.IsTrue(joinInfo.Redirected);
            Assert.AreEqual(TenantId, joinInfo.TenantId);
            Assert.IsInstanceOfType(joinInfo.MeetingInfo, typeof(JoinMeetingIdMeetingInfo));
        }

        [TestMethod]
        public async Task ResolveAsync_RejectsTeamsUnrelatedHost()
        {
            using var resolver = new TeamsMeetingUrlResolver(new HttpMessageInvoker(new SequenceHandler()), disposeClient: true);

            var ex = await Assert.ThrowsExceptionAsync<TeamsMeetingJoinException>(() =>
                resolver.ResolveAsync("https://example.com/meet/123?p=passcode", CancellationToken.None));

            Assert.AreEqual("unsupported_host", ex.Code);
        }

        [TestMethod]
        public async Task ResolveAsync_RejectsBlockedIpHost()
        {
            using var resolver = new TeamsMeetingUrlResolver(new HttpMessageInvoker(new SequenceHandler()), disposeClient: true);

            var ex = await Assert.ThrowsExceptionAsync<TeamsMeetingJoinException>(() =>
                resolver.ResolveAsync("https://127.0.0.1/meet/123?p=passcode", CancellationToken.None));

            Assert.AreEqual("blocked_host", ex.Code);
        }

        [TestMethod]
        public async Task ResolveAsync_RejectsRedirectToDisallowedDomain()
        {
            var handler = new SequenceHandler(Redirect("https://example.com/meet/123?p=passcode"));
            using var resolver = new TeamsMeetingUrlResolver(new HttpMessageInvoker(handler), disposeClient: true);

            var ex = await Assert.ThrowsExceptionAsync<TeamsMeetingJoinException>(() =>
                resolver.ResolveAsync("https://aka.ms/deci-join", CancellationToken.None));

            Assert.AreEqual("unsupported_host", ex.Code);
        }

        [TestMethod]
        public async Task ResolveAsync_RejectsRedirectLimitExceeded()
        {
            var handler = new SequenceHandler(
                Redirect("https://aka.ms/1"),
                Redirect("https://aka.ms/2"),
                Redirect("https://aka.ms/3"),
                Redirect("https://aka.ms/4"),
                Redirect("https://aka.ms/5"));
            using var resolver = new TeamsMeetingUrlResolver(new HttpMessageInvoker(handler), disposeClient: true);

            var ex = await Assert.ThrowsExceptionAsync<TeamsMeetingJoinException>(() =>
                resolver.ResolveAsync("https://aka.ms/deci-join", CancellationToken.None));

            Assert.AreEqual("too_many_redirects", ex.Code);
        }

        [TestMethod]
        public async Task Provider_RequiresTenantIdForMeetUrl()
        {
            var provider = new TeamsMeetingJoinInfoProvider(
                new TeamsMeetingUrlResolver(new HttpMessageInvoker(new SequenceHandler()), disposeClient: true),
                NullLogger<TeamsMeetingJoinInfoProvider>.Instance);

            var ex = await Assert.ThrowsExceptionAsync<TeamsMeetingJoinException>(() =>
                provider.GetJoinInfoAsync(
                    new JoinCallBody { MeetingUrl = "https://teams.microsoft.com/meet/123456789?p=passcode" },
                    CancellationToken.None));

            Assert.AreEqual("missing_tenant_id", ex.Code);
        }

        private static HttpResponseMessage Redirect(string location)
        {
            var response = new HttpResponseMessage(HttpStatusCode.Found);
            response.Headers.Location = new Uri(location);
            return response;
        }

        private sealed class SequenceHandler : HttpMessageHandler
        {
            private readonly Queue<HttpResponseMessage> _responses;

            public SequenceHandler(params HttpResponseMessage[] responses)
            {
                _responses = new Queue<HttpResponseMessage>(responses);
            }

            public int RequestCount { get; private set; }

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                RequestCount++;

                if (_responses.Count == 0)
                {
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
                }

                return Task.FromResult(_responses.Dequeue());
            }
        }
    }
}
