using EchoBot.Util;
using Microsoft.AspNetCore.Http;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Text;

namespace EchoBot.Tests
{
    [TestClass]
    public class HttpHelpersTests
    {
        [TestMethod]
        public void ToHttpRequestMessage_HandlesHttpsHostWithoutPort()
        {
            var request = CreateRequest("https", new HostString("ds-media-dev.japaneast.cloudapp.azure.com"));

            using var message = request.ToHttpRequestMessage();

            Assert.AreEqual("https", message.RequestUri!.Scheme);
            Assert.AreEqual("ds-media-dev.japaneast.cloudapp.azure.com", message.RequestUri.Host);
            Assert.AreEqual(443, message.RequestUri.Port);
            Assert.IsTrue(message.RequestUri.IsDefaultPort);
        }

        [TestMethod]
        public void ToHttpRequestMessage_HandlesHttpsHostWithPort443()
        {
            var request = CreateRequest("https", new HostString("example.com", 443));

            using var message = request.ToHttpRequestMessage();

            Assert.AreEqual("https", message.RequestUri!.Scheme);
            Assert.AreEqual("example.com", message.RequestUri.Host);
            Assert.AreEqual(443, message.RequestUri.Port);
            Assert.IsTrue(message.RequestUri.IsDefaultPort);
        }

        [TestMethod]
        public void ToHttpRequestMessage_HandlesHttpsHostWithNonStandardPort()
        {
            var request = CreateRequest("https", new HostString("example.com", 9443));

            using var message = request.ToHttpRequestMessage();

            Assert.AreEqual("https://example.com:9443/api/calling/notification", message.RequestUri!.AbsoluteUri);
        }

        [TestMethod]
        public void ToHttpRequestMessage_HandlesHttpHostWithoutPort()
        {
            var request = CreateRequest("http", new HostString("localhost"));

            using var message = request.ToHttpRequestMessage();

            Assert.AreEqual("http", message.RequestUri!.Scheme);
            Assert.AreEqual("localhost", message.RequestUri.Host);
            Assert.AreEqual(80, message.RequestUri.Port);
            Assert.IsTrue(message.RequestUri.IsDefaultPort);
        }

        [TestMethod]
        public void ToHttpRequestMessage_HandlesHttpHostWithExplicitPort()
        {
            var request = CreateRequest("http", new HostString("localhost", 3978));

            using var message = request.ToHttpRequestMessage();

            Assert.AreEqual("http://localhost:3978/api/calling/notification", message.RequestUri!.AbsoluteUri);
        }

        [TestMethod]
        public void ToHttpRequestMessage_PreservesPathBasePathAndQuery()
        {
            var request = CreateRequest(
                "https",
                new HostString("example.com"),
                pathBase: "/bot",
                path: "/api/calling/notification",
                queryString: new QueryString("?call=abc123&state=joined"));

            using var message = request.ToHttpRequestMessage();

            Assert.AreEqual("https://example.com/bot/api/calling/notification?call=abc123&state=joined", message.RequestUri!.AbsoluteUri);
        }

        [TestMethod]
        public void ToHttpRequestMessage_PreservesEncodedPathAndQuery()
        {
            var request = CreateRequest(
                "https",
                new HostString("example.com"),
                pathBase: PathString.FromUriComponent("/base%20path"),
                path: PathString.FromUriComponent("/api/calling/%E3%83%86%E3%82%B9%E3%83%88"),
                queryString: new QueryString("?room=a%2Fb&name=Deci%20Scope"));

            using var message = request.ToHttpRequestMessage();

            Assert.AreEqual("https://example.com/base%20path/api/calling/%E3%83%86%E3%82%B9%E3%83%88?room=a%2Fb&name=Deci%20Scope", message.RequestUri!.AbsoluteUri);
        }

        [TestMethod]
        public async Task ToHttpRequestMessage_PreservesMethodHeadersBodyAndContentType()
        {
            var request = CreateRequest("https", new HostString("example.com"));

            using var message = request.ToHttpRequestMessage();

            Assert.AreEqual(HttpMethod.Post, message.Method);
            Assert.IsTrue(message.Headers.TryGetValues("x-ms-call-chain-id", out var values));
            Assert.AreEqual("call-chain", values.Single());
            Assert.AreEqual("application/json", message.Content!.Headers.ContentType!.MediaType);
            Assert.AreEqual("{\"status\":\"ok\"}", await message.Content.ReadAsStringAsync());
        }

        [TestMethod]
        public void ToHttpRequestMessage_RejectsMissingHost()
        {
            var request = CreateRequest("https", default);

            var ex = Assert.ThrowsException<InvalidOperationException>(() => request.ToHttpRequestMessage());

            StringAssert.Contains(ex.Message, "host");
        }

        [TestMethod]
        public void ToHttpRequestMessage_RejectsInvalidHost()
        {
            var request = CreateRequest("https", new HostString("bad host"));

            var ex = Assert.ThrowsException<InvalidOperationException>(() => request.ToHttpRequestMessage());

            StringAssert.Contains(ex.Message, "URI");
        }

        [TestMethod]
        public void ToHttpRequestMessage_RejectsInvalidScheme()
        {
            var request = CreateRequest("ftp", new HostString("example.com"));

            var ex = Assert.ThrowsException<InvalidOperationException>(() => request.ToHttpRequestMessage());

            StringAssert.Contains(ex.Message, "scheme");
        }

        private static HttpRequest CreateRequest(
            string scheme,
            HostString host,
            PathString? pathBase = null,
            PathString? path = null,
            QueryString? queryString = null)
        {
            var context = new DefaultHttpContext();
            var body = Encoding.UTF8.GetBytes("{\"status\":\"ok\"}");

            context.Request.Method = HttpMethods.Post;
            context.Request.Scheme = scheme;
            context.Request.Host = host;
            context.Request.PathBase = pathBase ?? PathString.Empty;
            context.Request.Path = path ?? new PathString("/api/calling/notification");
            context.Request.QueryString = queryString ?? QueryString.Empty;
            context.Request.Body = new MemoryStream(body);
            context.Request.ContentType = "application/json";
            context.Request.Headers["x-ms-call-chain-id"] = "call-chain";

            return context.Request;
        }
    }
}
