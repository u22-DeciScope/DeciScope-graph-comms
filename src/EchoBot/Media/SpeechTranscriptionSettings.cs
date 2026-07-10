namespace EchoBot.Media
{
    public sealed class SpeechTranscriptionSettings
    {
        public SpeechTranscriptionSettings(
            string key,
            string region,
            string recognitionLanguage,
            bool logTranscripts,
            int audioQueueCapacity,
            int segmentationSilenceTimeoutMs)
        {
            Key = key;
            Region = region;
            RecognitionLanguage = recognitionLanguage;
            LogTranscripts = logTranscripts;
            AudioQueueCapacity = audioQueueCapacity;
            SegmentationSilenceTimeoutMs = segmentationSilenceTimeoutMs;
        }

        public string Key { get; }

        public string Region { get; }

        public string RecognitionLanguage { get; }

        public bool LogTranscripts { get; }

        public int AudioQueueCapacity { get; }

        public int SegmentationSilenceTimeoutMs { get; }

        public static SpeechTranscriptionSettings FromAppSettings(AppSettings settings)
        {
            var key = FirstNonEmpty(settings.SpeechKey, settings.SpeechConfigKey);
            var region = FirstNonEmpty(settings.SpeechRegion, settings.SpeechConfigRegion);
            var language = FirstNonEmpty(settings.SpeechRecognitionLanguage, settings.BotLanguage, "ja-JP");
            var capacity = settings.SpeechAudioQueueCapacity > 0 ? settings.SpeechAudioQueueCapacity : 500;
            var segmentationSilenceTimeoutMs = Clamp(
                settings.SpeechSegmentationSilenceTimeoutMs > 0
                    ? settings.SpeechSegmentationSilenceTimeoutMs
                    : 650,
                500,
                800);

            if (string.IsNullOrWhiteSpace(key))
            {
                throw new InvalidOperationException("Speech transcription is enabled, but SpeechKey is not configured.");
            }

            if (string.IsNullOrWhiteSpace(region))
            {
                throw new InvalidOperationException("Speech transcription is enabled, but SpeechRegion is not configured.");
            }

            return new SpeechTranscriptionSettings(
                key,
                region,
                language,
                settings.LogTranscripts,
                capacity,
                segmentationSilenceTimeoutMs);
        }

        private static string FirstNonEmpty(params string?[] values)
        {
            foreach (var value in values)
            {
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value;
                }
            }

            return string.Empty;
        }

        private static int Clamp(int value, int min, int max)
        {
            if (value < min)
            {
                return min;
            }

            if (value > max)
            {
                return max;
            }

            return value;
        }
    }
}
