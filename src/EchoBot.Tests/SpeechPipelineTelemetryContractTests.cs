using EchoBot.Bot;
using EchoBot.Media;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EchoBot.Tests
{
    [TestClass]
    public class SpeechPipelineTelemetryContractTests
    {
        [TestMethod]
        public void Snapshot_ExposesActiveRecognizerIdentityGenerationAndRecognitionTimes()
        {
            var type = typeof(SpeechPipelineSnapshot);

            Assert.IsNotNull(type.GetProperty("RecognizerInstanceIdHash"));
            Assert.IsNotNull(type.GetProperty("PipelineGeneration"));
            Assert.IsNotNull(type.GetProperty("LastRecognizerStartedAtUtc"));
            Assert.IsNotNull(type.GetProperty("LastSpeechPartialAtUtc"));
            Assert.IsNotNull(type.GetProperty("LastSpeechFinalAtUtc"));
        }

        [TestMethod]
        public void HeartbeatMetrics_ExposeAggregatedActivePipelineFlags()
        {
            var type = typeof(BotMediaMetricsSnapshot);

            Assert.IsNotNull(type.GetProperty("SpeechPipelineReady"));
            Assert.IsNotNull(type.GetProperty("SpeechStarted"));
            Assert.IsNotNull(type.GetProperty("RecognizerCreated"));
            Assert.IsNotNull(type.GetProperty("PushStreamOpen"));
            Assert.IsNotNull(type.GetProperty("PipelineGeneration"));
            Assert.IsNotNull(type.GetProperty("RecognizerInstanceIdHash"));
        }

        [TestMethod]
        public void Aggregate_PrefersReadySpeakerRecognizerOverStoppedMixedFallback()
        {
            var now = DateTimeOffset.UtcNow;
            var stoppedMixedFallback = new SpeechPipelineSnapshot(
                serviceAvailable: true,
                started: false,
                acceptingFrames: false,
                recognizerCreated: false,
                pushStreamOpen: false,
                droppedFrames: 0,
                recognizerInstanceIdHash: "mixed-hash",
                pipelineGeneration: 3,
                lastRecognizerStartedAtUtc: now.AddSeconds(-1),
                lastSpeechPartialAtUtc: now,
                lastSpeechFinalAtUtc: now);
            var activeSpeaker = new SpeechPipelineSnapshot(
                serviceAvailable: true,
                started: true,
                acceptingFrames: true,
                recognizerCreated: true,
                pushStreamOpen: true,
                droppedFrames: 0,
                recognizerInstanceIdHash: "speaker-hash",
                pipelineGeneration: 1,
                lastRecognizerStartedAtUtc: now.AddMinutes(-1),
                lastSpeechPartialAtUtc: now.AddSeconds(-10),
                lastSpeechFinalAtUtc: now.AddSeconds(-20));

            var aggregate = SpeechPipelineSnapshot.Aggregate(new[] { stoppedMixedFallback, activeSpeaker });

            Assert.IsTrue(aggregate.Ready);
            Assert.AreEqual("speaker-hash", aggregate.RecognizerInstanceIdHash);
            Assert.AreEqual(1L, aggregate.PipelineGeneration);
        }

        [TestMethod]
        public void Aggregate_DoesNotCombineFlagsFromDifferentRecognizerInstances()
        {
            var startedWithoutResources = new SpeechPipelineSnapshot(
                serviceAvailable: true,
                started: true,
                acceptingFrames: true,
                recognizerCreated: false,
                pushStreamOpen: false,
                droppedFrames: 0,
                recognizerInstanceIdHash: "started-hash");
            var resourcesWithoutStart = new SpeechPipelineSnapshot(
                serviceAvailable: true,
                started: false,
                acceptingFrames: false,
                recognizerCreated: true,
                pushStreamOpen: true,
                droppedFrames: 0,
                recognizerInstanceIdHash: "resource-hash");

            var aggregate = SpeechPipelineSnapshot.Aggregate(new[] { startedWithoutResources, resourcesWithoutStart });

            Assert.IsFalse(aggregate.Ready);
            Assert.AreEqual("started-hash", aggregate.RecognizerInstanceIdHash);
            Assert.IsTrue(aggregate.Started);
            Assert.IsFalse(aggregate.RecognizerCreated);
            Assert.IsFalse(aggregate.PushStreamOpen);
        }

        [TestMethod]
        public void Mutation_ActiveRecognizerTelemetryChangedToFalseBreaksCallbackInvariant()
        {
            Assert.IsTrue(SpeechPipelineSnapshot.CallbackStateInvariantSatisfied(
                started: true,
                recognizerCreated: true,
                pushStreamOpen: true));

            Assert.IsFalse(SpeechPipelineSnapshot.CallbackStateInvariantSatisfied(
                started: false,
                recognizerCreated: false,
                pushStreamOpen: false));
        }
    }
}
