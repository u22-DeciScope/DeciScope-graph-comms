using System.Text.Json;
using EchoBot.Bot;
using EchoBot.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EchoBot.Tests
{
    [TestClass]
    public class BotHeartbeatUpdateTests
    {
        [TestMethod]
        public void Serialize_WithMetrics_UsesExpectedCamelCaseKeys()
        {
            var recordedAt = new DateTimeOffset(2024, 1, 2, 3, 4, 5, TimeSpan.Zero);
            var metrics = new BotMediaMetricsSnapshot
            {
                LastAudioFrameAtUtc = recordedAt,
                LastNonZeroAudioAtUtc = recordedAt,
                LastPeakAmplitude = 1234,
                LastRmsAmplitude = 56.7,
                AudioFrameCount = 100,
                FramesSinceLastNonZeroAudio = 5,
                SecondsSinceLastNonZeroAudio = 3,
                ActiveSpeakerRecognizerCount = 2,
                MixedFallbackActive = true,
                UnmixedAudioSeen = true,
                LastNonEmptyTranscriptAtUtc = recordedAt,
                LastFinalTranscriptAtUtc = recordedAt,
                LastAudioSocketReceiveStallAtUtc = recordedAt,
                AudioSocketReceiveStallCount = 1,
                AudioStalled = false,
                SpeechPipelineReady = true,
                SpeechStarted = true,
                SpeechAcceptingFrames = true,
                RecognizerCreated = true,
                PushStreamOpen = true,
                PipelineGeneration = 4,
                RecognizerInstanceIdHash = "abc123",
                LastRecognizerStartedAtUtc = recordedAt,
                LastSpeechPartialAtUtc = recordedAt,
                LastSpeechFinalAtUtc = recordedAt,
            };

            var update = new BotHeartbeatUpdate("call-1", metrics);
            var json = JsonSerializer.Serialize(update, new JsonSerializerOptions(JsonSerializerDefaults.Web));

            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;

            Assert.AreEqual("call-1", root.GetProperty("botCallId").GetString());
            Assert.IsTrue(root.TryGetProperty("lastAudioFrameAtUtc", out _));
            Assert.IsTrue(root.TryGetProperty("lastNonZeroAudioAtUtc", out _));
            Assert.IsTrue(root.TryGetProperty("lastNonEmptyTranscriptAtUtc", out _));
            Assert.IsTrue(root.TryGetProperty("lastFinalTranscriptAtUtc", out _));
            Assert.AreEqual(1234, root.GetProperty("lastPeakAmplitude").GetInt32());
            Assert.AreEqual(56.7, root.GetProperty("lastRmsAmplitude").GetDouble());
            Assert.AreEqual(100, root.GetProperty("audioFrameCount").GetInt64());
            Assert.AreEqual(5, root.GetProperty("framesSinceLastNonZeroAudio").GetInt64());
            Assert.AreEqual(3, root.GetProperty("secondsSinceLastNonZeroAudio").GetInt32());
            Assert.AreEqual(2, root.GetProperty("activeSpeakerRecognizerCount").GetInt32());
            Assert.IsTrue(root.GetProperty("mixedFallbackActive").GetBoolean());
            Assert.IsTrue(root.GetProperty("unmixedAudioSeen").GetBoolean());
            Assert.IsTrue(root.TryGetProperty("lastAudioSocketReceiveStallAtUtc", out _));
            Assert.AreEqual(1, root.GetProperty("audioSocketReceiveStallCount").GetInt64());
            Assert.IsFalse(root.GetProperty("audioStalled").GetBoolean());
            Assert.IsTrue(root.GetProperty("speechPipelineReady").GetBoolean());
            Assert.IsTrue(root.GetProperty("speechStarted").GetBoolean());
            Assert.IsTrue(root.GetProperty("speechAcceptingFrames").GetBoolean());
            Assert.IsTrue(root.GetProperty("recognizerCreated").GetBoolean());
            Assert.IsTrue(root.GetProperty("pushStreamOpen").GetBoolean());
            Assert.AreEqual(4, root.GetProperty("pipelineGeneration").GetInt64());
            Assert.AreEqual("abc123", root.GetProperty("recognizerInstanceIdHash").GetString());
            Assert.IsTrue(root.TryGetProperty("lastRecognizerStartedAtUtc", out _));
            Assert.IsTrue(root.TryGetProperty("lastSpeechPartialAtUtc", out _));
            Assert.IsTrue(root.TryGetProperty("lastSpeechFinalAtUtc", out _));
        }

        [TestMethod]
        public void Serialize_WithoutMetrics_OmitsMetricFields()
        {
            var update = new BotHeartbeatUpdate("call-1");
            var json = JsonSerializer.Serialize(update, new JsonSerializerOptions(JsonSerializerDefaults.Web));

            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;

            Assert.AreEqual("call-1", root.GetProperty("botCallId").GetString());
            Assert.IsFalse(root.TryGetProperty("lastAudioFrameAtUtc", out _));
            Assert.IsFalse(root.TryGetProperty("audioFrameCount", out _));
            Assert.IsFalse(root.TryGetProperty("audioStalled", out _));
            Assert.IsFalse(root.TryGetProperty("speechPipelineReady", out _));
            Assert.IsFalse(root.TryGetProperty("recognizerInstanceIdHash", out _));
        }
    }
}
