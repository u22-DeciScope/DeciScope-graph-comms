namespace EchoBot.Services
{
    public sealed class TranscriptForwardingOptions
    {
        public const string EnabledEnvironmentVariable = "DECISCOPE_TRANSCRIPT_FORWARD_ENABLED";
        public const string ApiUrlEnvironmentVariable = "DECISCOPE_TRANSCRIPT_API_URL";
        public const string ApiKeyEnvironmentVariable = "DECISCOPE_TRANSCRIPT_API_KEY";
        public const string TimeoutSecondsEnvironmentVariable = "DECISCOPE_TRANSCRIPT_API_TIMEOUT_SECONDS";

        private const int DefaultTimeoutSeconds = 5;

        private TranscriptForwardingOptions(
            bool requestedEnabled,
            bool enabled,
            Uri? apiUrl,
            string? apiKey,
            int timeoutSeconds,
            string reason)
        {
            RequestedEnabled = requestedEnabled;
            Enabled = enabled;
            ApiUrl = apiUrl;
            ApiKey = apiKey;
            TimeoutSeconds = timeoutSeconds;
            Reason = reason;
        }

        public bool RequestedEnabled { get; }

        public bool Enabled { get; }

        public Uri? ApiUrl { get; }

        public string? ApiKey { get; }

        public int TimeoutSeconds { get; }

        public string Reason { get; }

        public bool ApiKeyConfigured => !string.IsNullOrWhiteSpace(ApiKey);

        public static TranscriptForwardingOptions FromEnvironment()
        {
            return FromValues(
                Environment.GetEnvironmentVariable(EnabledEnvironmentVariable),
                Environment.GetEnvironmentVariable(ApiUrlEnvironmentVariable),
                Environment.GetEnvironmentVariable(ApiKeyEnvironmentVariable),
                Environment.GetEnvironmentVariable(TimeoutSecondsEnvironmentVariable));
        }

        public static TranscriptForwardingOptions FromValues(
            string? enabledValue,
            string? apiUrlValue,
            string? apiKeyValue,
            string? timeoutSecondsValue)
        {
            var timeoutSeconds = ParseTimeoutSeconds(timeoutSecondsValue);
            if (!bool.TryParse(enabledValue, out var requestedEnabled) || !requestedEnabled)
            {
                return new TranscriptForwardingOptions(false, false, null, null, timeoutSeconds, "ForwardingDisabled");
            }

            if (string.IsNullOrWhiteSpace(apiUrlValue))
            {
                return new TranscriptForwardingOptions(true, false, null, apiKeyValue, timeoutSeconds, "ApiUrlMissing");
            }

            if (!Uri.TryCreate(apiUrlValue, UriKind.Absolute, out var apiUrl)
                || (apiUrl.Scheme != Uri.UriSchemeHttp && apiUrl.Scheme != Uri.UriSchemeHttps))
            {
                return new TranscriptForwardingOptions(true, false, null, apiKeyValue, timeoutSeconds, "ApiUrlInvalid");
            }

            if (string.IsNullOrWhiteSpace(apiKeyValue))
            {
                return new TranscriptForwardingOptions(true, false, apiUrl, null, timeoutSeconds, "ApiKeyMissing");
            }

            return new TranscriptForwardingOptions(true, true, apiUrl, apiKeyValue, timeoutSeconds, "Configured");
        }

        private static int ParseTimeoutSeconds(string? value)
        {
            return int.TryParse(value, out var timeoutSeconds) && timeoutSeconds > 0
                ? timeoutSeconds
                : DefaultTimeoutSeconds;
        }
    }
}
