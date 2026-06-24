// ***********************************************************************
// Assembly         : EchoBot.Util
// Author           : bcage29
// Created          : 09-07-2020
//
// Last Modified By : bcage29
// Last Modified On : 02-28-2022
// ***********************************************************************
// <copyright file="HttpHelpers.cs" company="Microsoft">
//     Copyright ©  2023
// </copyright>
// <summary></summary>
// ***********************************************************************>
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Extensions;

namespace EchoBot.Util
{
    public static class HttpHelpers
    {
        public static HttpRequestMessage ToHttpRequestMessage(this HttpRequest req)
        {
            ArgumentNullException.ThrowIfNull(req);

            return new HttpRequestMessage()
                .SetMethod(req)
                .SetAbsoluteUri(req)
                .SetHeaders(req)
                .SetContent(req)
                .SetContentType(req);
        }

        private static HttpRequestMessage SetAbsoluteUri(this HttpRequestMessage msg, HttpRequest req)
            => msg.Set(m => m.RequestUri = BuildAbsoluteUri(req));

        private static Uri BuildAbsoluteUri(HttpRequest req)
        {
            if (string.IsNullOrWhiteSpace(req.Scheme))
            {
                throw new InvalidOperationException("The incoming HTTP request has no scheme.");
            }

            if (!req.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
                && !req.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("The incoming HTTP request scheme is not supported.");
            }

            if (!req.Host.HasValue)
            {
                throw new InvalidOperationException("The incoming HTTP request has no host.");
            }

            try
            {
                var encodedUrl = req.GetEncodedUrl();
                if (Uri.TryCreate(encodedUrl, UriKind.Absolute, out var uri))
                {
                    return uri;
                }
            }
            catch (Exception ex) when (ex is ArgumentException || ex is InvalidOperationException || ex is UriFormatException)
            {
                throw new InvalidOperationException("The incoming HTTP request URI could not be converted to an absolute URI.", ex);
            }

            throw new InvalidOperationException("The incoming HTTP request URI could not be converted to an absolute URI.");
        }

        private static HttpRequestMessage SetMethod(this HttpRequestMessage msg, HttpRequest req)
            => msg.Set(m => m.Method = new HttpMethod(req.Method));

        private static HttpRequestMessage SetHeaders(this HttpRequestMessage msg, HttpRequest req)
            => req.Headers.Aggregate(msg, (acc, h) => acc.Set(m => m.Headers.TryAddWithoutValidation(h.Key, h.Value.AsEnumerable())));

        private static HttpRequestMessage SetContent(this HttpRequestMessage msg, HttpRequest req)
            => msg.Set(m => m.Content = new StreamContent(req.Body));

        private static HttpRequestMessage SetContentType(this HttpRequestMessage msg, HttpRequest req)
        {
            var contentType = req.ContentType;
            if (string.IsNullOrWhiteSpace(contentType))
            {
                return msg;
            }

            return msg.Set(m => m.Content!.Headers.Add("Content-Type", contentType));
        }

        private static HttpRequestMessage Set(this HttpRequestMessage msg, Action<HttpRequestMessage> config, bool applyIf = true)
        {
            if (applyIf)
            {
                config.Invoke(msg);
            }

            return msg;
        }
    }
}
