namespace EchoBot.Media
{
    public sealed class SpeechTranscriptionSettings
    {
        public SpeechTranscriptionSettings(
            string key,
            string region,
            string recognitionLanguage,
            bool logTranscripts,
            int audioQueueCapacity)
        {
            Key = key;
            Region = region;
            RecognitionLanguage = recognitionLanguage;
            LogTranscripts = logTranscripts;
            AudioQueueCapacity = audioQueueCapacity;
        }

        public string Key { get; }

        public string Region { get; }

        public string RecognitionLanguage { get; }

        public bool LogTranscripts { get; }

        public int AudioQueueCapacity { get; }

        public static SpeechTranscriptionSettings FromAppSettings(AppSettings settings)
        {
            var key = FirstNonEmpty(settings.SpeechKey, settings.SpeechConfigKey);
            var region = FirstNonEmpty(settings.SpeechRegion, settings.SpeechConfigRegion);
            var language = FirstNonEmpty(settings.SpeechRecognitionLanguage, settings.BotLanguage, "ja-JP");
            var capacity = settings.SpeechAudioQueueCapacity > 0 ? settings.SpeechAudioQueueCapacity : 500;

            if (string.IsNullOrWhiteSpace(key))
            {
                throw new InvalidOperationException("Speech transcription is enabled, but SpeechKey is not configured.");
            }

            if (string.IsNullOrWhiteSpace(region))
            {
                throw new InvalidOperationException("Speech transcription is enabled, but SpeechRegion is not configured.");
            }

            return new SpeechTranscriptionSettings(key, region, language, settings.LogTranscripts, capacity);
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
    }
}
