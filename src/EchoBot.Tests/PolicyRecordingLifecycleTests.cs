using EchoBot.Bot;
using Microsoft.Graph.Models;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EchoBot.Tests
{
    [TestClass]
    public class PolicyRecordingLifecycleTests
    {
        [TestMethod]
        public void ShouldStartRecording_ReturnsTrueOnlyForPolicyRecordingEstablishedTransition()
        {
            Assert.IsTrue(PolicyRecordingLifecycle.ShouldStartRecording(
                CallOrigin.PolicyRecordingIncoming,
                CallState.Establishing,
                CallState.Established));

            Assert.IsFalse(PolicyRecordingLifecycle.ShouldStartRecording(
                CallOrigin.OutboundJoin,
                CallState.Establishing,
                CallState.Established));

            Assert.IsFalse(PolicyRecordingLifecycle.ShouldStartRecording(
                CallOrigin.PolicyRecordingIncoming,
                CallState.Establishing,
                CallState.Establishing));
        }

        [TestMethod]
        public void ShouldSkipRecording_ReturnsTrueForOutboundJoin()
        {
            Assert.IsTrue(PolicyRecordingLifecycle.ShouldSkipRecording(CallOrigin.OutboundJoin));
            Assert.IsFalse(PolicyRecordingLifecycle.ShouldSkipRecording(CallOrigin.PolicyRecordingIncoming));
        }

        [TestMethod]
        public void CanPersistMediaOrDerivedData_ReturnsTrueOnlyAfterRecordingConfirmed()
        {
            Assert.IsTrue(PolicyRecordingLifecycle.CanPersistMediaOrDerivedData(
                CallOrigin.PolicyRecordingIncoming,
                PolicyRecordingCallState.RecordingStatusConfirmed,
                terminationStarted: false));

            Assert.IsFalse(PolicyRecordingLifecycle.CanPersistMediaOrDerivedData(
                CallOrigin.OutboundJoin,
                PolicyRecordingCallState.RecordingStatusConfirmed,
                terminationStarted: false));

            Assert.IsFalse(PolicyRecordingLifecycle.CanPersistMediaOrDerivedData(
                CallOrigin.PolicyRecordingIncoming,
                PolicyRecordingCallState.Established,
                terminationStarted: false));

            Assert.IsFalse(PolicyRecordingLifecycle.CanPersistMediaOrDerivedData(
                CallOrigin.PolicyRecordingIncoming,
                PolicyRecordingCallState.RecordingStatusConfirmed,
                terminationStarted: true));
        }
    }
}
