using System.Net;
using System.Net.Http.Headers;

namespace EchoBot.Meetings
{
    public sealed class TeamsMeetingUrlResolver : IDisposable
    {
        private const int MaxRedirects = 5;
        private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(5);
        private static readonly HashSet<string> TeamsHosts = new(StringComparer.OrdinalIgnoreCase)
        {
            "teams.microsoft.com",
            "teams.live.com",
        };

        private static readonly HashSet<string> AllowedRedirectHosts = new(StringComparer.OrdinalIgnoreCase)
        {
            "aka.ms",
            "go.microsoft.com",
            "teams.microsoft.com",
            "teams.live.com",
        };

        private readonly HttpMessageInvoker _httpClient;
        private readonly bool _disposeClient;

        public TeamsMeetingUrlResolver()
            : this(CreateDefaultClient(), disposeClient: true)
        {
        }

        public TeamsMeetingUrlResolver(HttpMessageInvoker httpClient, bool disposeClient = false)
        {
            _httpClient = httpClient;
            _disposeClient = disposeClient;
        }

        public async Task<(Uri ResolvedUrl, bool Redirected)> ResolveAsync(string? joinUrl, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(joinUrl))
            {
                throw new TeamsMeetingJoinException("missing_join_url", "A Teams meeting URL is required.");
            }

            if (!Uri.TryCreate(joinUrl.Trim(), UriKind.Absolute, out var currentUri))
            {
                throw new TeamsMeetingJoinException("invalid_url", "The meeting URL must be an absolute HTTPS URL.");
            }

            ValidateAllowedUrl(currentUri, allowRedirectorHost: true);

            if (IsTeamsHost(currentUri.Host))
            {
                return (currentUri, false);
            }

            var redirected = false;
            for (var redirectCount = 0; redirectCount < MaxRedirects; redirectCount++)
            {
                using var request = new HttpRequestMessage(HttpMethod.Head, currentUri);
                request.Headers.UserAgent.Add(new ProductInfoHeaderValue("DeciScopeTeamsBot", "1.0"));

                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(RequestTimeout);

                HttpResponseMessage response;
                try
                {
                    response = await _httpClient.SendAsync(request, timeout.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
                {
                    throw new TeamsMeetingJoinException("redirect_timeout", "Timed out while resolving the meeting URL redirect.", ex);
                }
                catch (HttpRequestException ex)
                {
                    throw new TeamsMeetingJoinException("redirect_failed", "Failed to resolve the meeting URL redirect.", ex);
                }

                using (response)
                {
                    if (!IsRedirect(response.StatusCode))
                    {
                        throw new TeamsMeetingJoinException("unsupported_redirect_url", "The meeting URL did not resolve to a supported Teams URL.");
                    }

                    if (response.Headers.Location == null)
                    {
                        throw new TeamsMeetingJoinException("missing_redirect_location", "The meeting URL redirect response did not include a Location header.");
                    }

                    currentUri = MakeAbsolute(currentUri, response.Headers.Location);
                    ValidateAllowedUrl(currentUri, allowRedirectorHost: true);
                    redirected = true;

                    if (IsTeamsHost(currentUri.Host))
                    {
                        return (currentUri, redirected);
                    }
                }
            }

            throw new TeamsMeetingJoinException("too_many_redirects", "The meeting URL exceeded the redirect limit.");
        }

        public void Dispose()
        {
            if (_disposeClient)
            {
                _httpClient.Dispose();
            }
        }

        private static HttpMessageInvoker CreateDefaultClient()
        {
            return new HttpClient(new HttpClientHandler
            {
                AllowAutoRedirect = false,
                UseCookies = false,
            });
        }

        private static void ValidateAllowedUrl(Uri uri, bool allowRedirectorHost)
        {
            if (!uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            {
                throw new TeamsMeetingJoinException("invalid_scheme", "The meeting URL must use HTTPS.");
            }

            if (uri.IsLoopback || IPAddress.TryParse(uri.Host, out _))
            {
                throw new TeamsMeetingJoinException("blocked_host", "The meeting URL host is not allowed.");
            }

            var hostAllowed = IsTeamsHost(uri.Host) || (allowRedirectorHost && AllowedRedirectHosts.Contains(uri.Host));
            if (!hostAllowed)
            {
                throw new TeamsMeetingJoinException("unsupported_host", "The meeting URL host is not a supported Teams or Microsoft redirect host.");
            }
        }

        private static bool IsTeamsHost(string host)
        {
            return TeamsHosts.Contains(host) || host.EndsWith(".teams.microsoft.com", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsRedirect(HttpStatusCode statusCode)
        {
            var numericStatusCode = (int)statusCode;
            return numericStatusCode >= 300 && numericStatusCode <= 399;
        }

        private static Uri MakeAbsolute(Uri currentUri, Uri location)
        {
            return location.IsAbsoluteUri ? location : new Uri(currentUri, location);
        }
    }
}
