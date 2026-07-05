using EchoBot.Media;
using EchoBot.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EchoBot.Tests
{
    [TestClass]
    public class SpeechStatusAggregatorTests
    {
        [TestMethod]
        public void Update_FirstReportIsAlwaysChanged()
        {
            var aggregator = new SpeechStatusAggregator();

            var result = aggregator.Update("speaker-a", BotMeetingStatus.Recording);

            Assert.IsTrue(result.Changed);
            Assert.AreEqual(BotMeetingStatus.Recording, result.AggregatedStatus);
        }

        [TestMethod]
        public void Update_OneThrottled_AggregatesToThrottled()
        {
            var aggregator = new SpeechStatusAggregator();
            aggregator.Update("speaker-a", BotMeetingStatus.Recording);

            var result = aggregator.Update("speaker-b", BotMeetingStatus.SpeechThrottled);

            Assert.IsTrue(result.Changed);
            Assert.AreEqual(BotMeetingStatus.SpeechThrottled, result.AggregatedStatus);
        }

        [TestMethod]
        public void Update_WhileThrottled_AnotherInstanceReportingRecording_DoesNotChangeAggregate()
        {
            var aggregator = new SpeechStatusAggregator();
            aggregator.Update("speaker-a", BotMeetingStatus.Recording);
            aggregator.Update("speaker-b", BotMeetingStatus.SpeechThrottled);

            // speaker-a re-reports recording (it was already recording); the aggregate is still
            // speech_throttled because speaker-b is still throttled, so no notification should fire.
            var result = aggregator.Update("speaker-a", BotMeetingStatus.Recording);

            Assert.IsFalse(result.Changed);
            Assert.AreEqual(BotMeetingStatus.SpeechThrottled, result.AggregatedStatus);
        }

        [TestMethod]
        public void Update_AllInstancesRecoverToRecording_ChangesExactlyOnce()
        {
            var aggregator = new SpeechStatusAggregator();
            aggregator.Update("speaker-a", BotMeetingStatus.Recording);
            aggregator.Update("speaker-b", BotMeetingStatus.SpeechThrottled);

            var recovered = aggregator.Update("speaker-b", BotMeetingStatus.Recording);
            Assert.IsTrue(recovered.Changed);
            Assert.AreEqual(BotMeetingStatus.Recording, recovered.AggregatedStatus);

            // Reporting recording again should not trigger a second "changed" notification.
            var again = aggregator.Update("speaker-a", BotMeetingStatus.Recording);
            Assert.IsFalse(again.Changed);
            Assert.AreEqual(BotMeetingStatus.Recording, again.AggregatedStatus);
        }

        [TestMethod]
        public void Update_ErrorTakesPriorityOverThrottled()
        {
            var aggregator = new SpeechStatusAggregator();
            aggregator.Update("speaker-a", BotMeetingStatus.SpeechThrottled);

            var result = aggregator.Update("speaker-b", BotMeetingStatus.SpeechError);

            Assert.IsTrue(result.Changed);
            Assert.AreEqual(BotMeetingStatus.SpeechError, result.AggregatedStatus);
        }

        [TestMethod]
        public void Update_ErrorTakesPriorityOverThrottled_RegardlessOfReportOrder()
        {
            var aggregator = new SpeechStatusAggregator();
            aggregator.Update("speaker-a", BotMeetingStatus.SpeechError);

            var result = aggregator.Update("speaker-b", BotMeetingStatus.SpeechThrottled);

            Assert.IsFalse(result.Changed);
            Assert.AreEqual(BotMeetingStatus.SpeechError, result.AggregatedStatus);
        }

        [TestMethod]
        public void Remove_LastInstance_AggregateBecomesNull()
        {
            var aggregator = new SpeechStatusAggregator();
            aggregator.Update("speaker-a", BotMeetingStatus.Recording);

            var result = aggregator.Remove("speaker-a");

            Assert.IsTrue(result.Changed);
            Assert.IsNull(result.AggregatedStatus);
        }

        [TestMethod]
        public void Remove_ErroredInstance_RecomputesFromRemainingInstances()
        {
            var aggregator = new SpeechStatusAggregator();
            aggregator.Update("speaker-a", BotMeetingStatus.Recording);
            aggregator.Update("speaker-b", BotMeetingStatus.SpeechError);

            var result = aggregator.Remove("speaker-b");

            Assert.IsTrue(result.Changed);
            Assert.AreEqual(BotMeetingStatus.Recording, result.AggregatedStatus);
        }

        [TestMethod]
        public void Remove_UnknownInstance_IsANoOp()
        {
            var aggregator = new SpeechStatusAggregator();
            aggregator.Update("speaker-a", BotMeetingStatus.Recording);

            var result = aggregator.Remove("speaker-does-not-exist");

            Assert.IsFalse(result.Changed);
            Assert.AreEqual(BotMeetingStatus.Recording, result.AggregatedStatus);
        }

        [TestMethod]
        public void Remove_ThenReAdd_RecomputesAggregateAgain()
        {
            var aggregator = new SpeechStatusAggregator();
            aggregator.Update("speaker-a", BotMeetingStatus.Recording);
            aggregator.Remove("speaker-a");

            var result = aggregator.Update("speaker-a", BotMeetingStatus.SpeechThrottled);

            Assert.IsTrue(result.Changed);
            Assert.AreEqual(BotMeetingStatus.SpeechThrottled, result.AggregatedStatus);
        }

        [TestMethod]
        public void Update_RejectsEmptyInstanceIdOrStatus()
        {
            var aggregator = new SpeechStatusAggregator();

            Assert.ThrowsException<ArgumentException>(() => aggregator.Update(string.Empty, BotMeetingStatus.Recording));
            Assert.ThrowsException<ArgumentException>(() => aggregator.Update("speaker-a", string.Empty));
        }
    }
}
