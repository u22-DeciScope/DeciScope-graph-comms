using EchoBot;
using EchoBot.Media;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EchoBot.Tests
{
    [TestClass]
    public class SpeechTranscriptionSettingsTests
    {
        [TestMethod]
        public void FromAppSettings_UsesNewSpeechSettings()
        {
            var settings = SpeechTranscriptionSettings.FromAppSettings(new AppSettings
            {
                SpeechKey = "key",
                SpeechRegion = "japaneast",
                SpeechRecognitionLanguage = "ja-JP",
                LogTranscripts = true,
                SpeechAudioQueueCapacity = 10,
                SpeechSegmentationSilenceTimeoutMs = 700
            });

            Assert.AreEqual("key", settings.Key);
            Assert.AreEqual("japaneast", settings.Region);
            Assert.AreEqual("ja-JP", settings.RecognitionLanguage);
            Assert.IsTrue(settings.LogTranscripts);
            Assert.AreEqual(10, settings.AudioQueueCapacity);
            Assert.AreEqual(700, settings.SegmentationSilenceTimeoutMs);
        }

        [TestMethod]
        public void FromAppSettings_FallsBackToExistingSpeechSettings()
        {
            var settings = SpeechTranscriptionSettings.FromAppSettings(new AppSettings
            {
                SpeechConfigKey = "old-key",
                SpeechConfigRegion = "old-region",
                BotLanguage = "en-US"
            });

            Assert.AreEqual("old-key", settings.Key);
            Assert.AreEqual("old-region", settings.Region);
            Assert.AreEqual("en-US", settings.RecognitionLanguage);
            Assert.AreEqual(500, settings.AudioQueueCapacity);
            Assert.AreEqual(650, settings.SegmentationSilenceTimeoutMs);
        }

        [TestMethod]
        public void FromAppSettings_ClampsSegmentationSilenceTimeoutToSupportedRange()
        {
            var low = SpeechTranscriptionSettings.FromAppSettings(new AppSettings
            {
                SpeechKey = "key",
                SpeechRegion = "region",
                SpeechSegmentationSilenceTimeoutMs = 300
            });
            var high = SpeechTranscriptionSettings.FromAppSettings(new AppSettings
            {
                SpeechKey = "key",
                SpeechRegion = "region",
                SpeechSegmentationSilenceTimeoutMs = 1200
            });

            Assert.AreEqual(500, low.SegmentationSilenceTimeoutMs);
            Assert.AreEqual(800, high.SegmentationSilenceTimeoutMs);
        }

        [TestMethod]
        public void FromAppSettings_DefaultsLanguageToJapanese()
        {
            var settings = SpeechTranscriptionSettings.FromAppSettings(new AppSettings
            {
                SpeechKey = "key",
                SpeechRegion = "region"
            });

            Assert.AreEqual("ja-JP", settings.RecognitionLanguage);
        }

        [TestMethod]
        public void FromAppSettings_ThrowsWhenSpeechKeyMissing()
        {
            var ex = Assert.ThrowsException<InvalidOperationException>(() =>
                SpeechTranscriptionSettings.FromAppSettings(new AppSettings { SpeechRegion = "region" }));

            Assert.AreEqual("Speech transcription is enabled, but SpeechKey is not configured.", ex.Message);
        }

        [TestMethod]
        public void FromAppSettings_ThrowsWhenSpeechRegionMissing()
        {
            var ex = Assert.ThrowsException<InvalidOperationException>(() =>
                SpeechTranscriptionSettings.FromAppSettings(new AppSettings { SpeechKey = "key" }));

            Assert.AreEqual("Speech transcription is enabled, but SpeechRegion is not configured.", ex.Message);
        }
    }
}
