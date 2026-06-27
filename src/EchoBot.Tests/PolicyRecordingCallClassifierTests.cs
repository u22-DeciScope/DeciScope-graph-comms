using EchoBot.Bot;
using Microsoft.Graph.Models;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EchoBot.Tests
{
    [TestClass]
    public class PolicyRecordingCallClassifierTests
    {
        [TestMethod]
        public void IsPolicyRecordingIncoming_ReturnsTrueForIncomingCallWithIncomingContext()
        {
            var call = new Call
            {
                Direction = CallDirection.Incoming,
                IncomingContext = new IncomingContext(),
            };

            Assert.IsTrue(PolicyRecordingCallClassifier.IsPolicyRecordingIncoming(call));
        }

        [TestMethod]
        public void IsPolicyRecordingIncoming_ReturnsFalseWithoutIncomingContext()
        {
            var call = new Call
            {
                Direction = CallDirection.Incoming,
            };

            Assert.IsFalse(PolicyRecordingCallClassifier.IsPolicyRecordingIncoming(call));
        }

        [TestMethod]
        public void IsPolicyRecordingIncoming_ReturnsFalseForOutgoingCall()
        {
            var call = new Call
            {
                Direction = CallDirection.Outgoing,
                IncomingContext = new IncomingContext(),
            };

            Assert.IsFalse(PolicyRecordingCallClassifier.IsPolicyRecordingIncoming(call));
        }

        [TestMethod]
        public void ShouldWarnAnswerLatency_UsesConfiguredThreshold()
        {
            Assert.IsFalse(PolicyRecordingCallClassifier.ShouldWarnAnswerLatency(2999, 3000));
            Assert.IsTrue(PolicyRecordingCallClassifier.ShouldWarnAnswerLatency(3000, 3000));
        }

        [TestMethod]
        public void ShouldWarnAnswerLatency_DefaultsInvalidThreshold()
        {
            Assert.IsTrue(PolicyRecordingCallClassifier.ShouldWarnAnswerLatency(3000, 0));
        }
    }
}
