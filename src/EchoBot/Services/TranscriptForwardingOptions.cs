namespace EchoBot.Services
{
    public sealed class TranscriptForwardingOptions
    {
        public const string EnabledEnvironmentVariable = "DECISCOPE_TRANSCRIPT_FORWARD_ENABLED";
        public const string ApiUrlEnvironmentVariable = "DECISCOPE_TRANSCRIPT_API_URL";
        public const string ApiKeyEnvironmentVariable = "DECISCOPE_TRANSCRIPT_API_KEY";
        public const string TimeoutSecondsEnvironmentVariable = "DECISCOPE_TRANSCRIPT_API_TIMEOUT_SECONDS";
        public const string MaxRetryAttemptsEnvironmentVariable = "DECISCOPE_TRANSCRIPT_API_MAX_RETRY_ATTEMPTS";
        public const string QueueCapacityEnvironmentVariable = "DECISCOPE_TRANSCRIPT_FORWARD_QUEUE_CAPACITY";

        private const int DefaultTimeoutSeconds = 5;
        private const int DefaultMaxRetryAttempts = 3;
        private const int DefaultQueueCapacity = 1000;

        private TranscriptForwardingOptions(
            bool requestedEnabled,
            bool enabled,
            Uri? apiUrl,
            string? apiKey,
            int timeoutSeconds,
            int maxRetryAttempts,
            int queueCapacity,
            string reason)
        {
            RequestedEnabled = requestedEnabled;
            Enabled = enabled;
            ApiUrl = apiUrl;
            ApiKey = apiKey;
            TimeoutSeconds = timeoutSeconds;
            MaxRetryAttempts = maxRetryAttempts;
            QueueCapacity = queueCapacity;
            Reason = reason;
        }

        public bool RequestedEnabled { get; }

        public bool Enabled { get; }

        public Uri? ApiUrl { get; }

        public string? ApiKey { get; }

        public int TimeoutSeconds { get; }

        public int MaxRetryAttempts { get; }

        public int QueueCapacity { get; }

        public string Reason { get; }

        public bool ApiKeyConfigured => !string.IsNullOrWhiteSpace(ApiKey);

        public static TranscriptForwardingOptions FromEnvironment()
        {
            return FromValues(
                Environment.GetEnvironmentVariable(EnabledEnvironmentVariable),
                Environment.GetEnvironmentVariable(ApiUrlEnvironmentVariable),
                Environment.GetEnvironmentVariable(ApiKeyEnvironmentVariable),
                Environment.GetEnvironmentVariable(TimeoutSecondsEnvironmentVariable),
                Environment.GetEnvironmentVariable(MaxRetryAttemptsEnvironmentVariable),
                Environment.GetEnvironmentVariable(QueueCapacityEnvironmentVariable));
        }

        public static TranscriptForwardingOptions FromValues(
            string? enabledValue,
            string? apiUrlValue,
            string? apiKeyValue,
            string? timeoutSecondsValue,
            string? maxRetryAttemptsValue = null,
            string? queueCapacityValue = null)
        {
            var timeoutSeconds = ParseTimeoutSeconds(timeoutSecondsValue);
            var maxRetryAttempts = ParsePositiveInt(maxRetryAttemptsValue, DefaultMaxRetryAttempts);
            var queueCapacity = ParsePositiveInt(queueCapacityValue, DefaultQueueCapacity);
            if (!bool.TryParse(enabledValue, out var requestedEnabled) || !requestedEnabled)
            {
                return new TranscriptForwardingOptions(false, false, null, null, timeoutSeconds, maxRetryAttempts, queueCapacity, "ForwardingDisabled");
            }

            if (string.IsNullOrWhiteSpace(apiUrlValue))
            {
                return new TranscriptForwardingOptions(true, false, null, apiKeyValue, timeoutSeconds, maxRetryAttempts, queueCapacity, "ApiUrlMissing");
            }

            if (!Uri.TryCreate(apiUrlValue, UriKind.Absolute, out var apiUrl)
                || (apiUrl.Scheme != Uri.UriSchemeHttp && apiUrl.Scheme != Uri.UriSchemeHttps))
            {
                return new TranscriptForwardingOptions(true, false, null, apiKeyValue, timeoutSeconds, maxRetryAttempts, queueCapacity, "ApiUrlInvalid");
            }

            if (string.IsNullOrWhiteSpace(apiKeyValue))
            {
                return new TranscriptForwardingOptions(true, false, apiUrl, null, timeoutSeconds, maxRetryAttempts, queueCapacity, "ApiKeyMissing");
            }

            return new TranscriptForwardingOptions(true, true, apiUrl, apiKeyValue, timeoutSeconds, maxRetryAttempts, queueCapacity, "Configured");
        }

        private static int ParseTimeoutSeconds(string? value)
        {
            return ParsePositiveInt(value, DefaultTimeoutSeconds);
        }

        private static int ParsePositiveInt(string? value, int defaultValue)
        {
            return int.TryParse(value, out var parsed) && parsed > 0
                ? parsed
                : defaultValue;
        }
    }
}
