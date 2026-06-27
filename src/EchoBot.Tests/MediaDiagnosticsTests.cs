using EchoBot.Bot;
using Microsoft.Graph.Models;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EchoBot.Tests
{
    [TestClass]
    public class MediaDiagnosticsTests
    {
        [TestMethod]
        public void EstablishedTransition_ReturnsTrueWithoutThrowing()
        {
            var established = CallDiagnostics.IsEstablishedTransition(CallState.Establishing, CallState.Established);

            Assert.IsTrue(established);
        }

        [TestMethod]
        public void EstablishedTransition_AllowsNullOldState()
        {
            var established = CallDiagnostics.IsEstablishedTransition(null, CallState.Established);

            Assert.IsTrue(established);
        }

        [TestMethod]
        public void EstablishedTransition_AllowsNullNewState()
        {
            var established = CallDiagnostics.IsEstablishedTransition(CallState.Establishing, null);

            Assert.IsFalse(established);
        }

        [TestMethod]
        public void TerminatedFromEstablished_ReturnsTrueOnlyForEstablishedToTerminated()
        {
            Assert.IsTrue(CallDiagnostics.IsTerminatedFromEstablished(CallState.Established, CallState.Terminated));
            Assert.IsFalse(CallDiagnostics.IsTerminatedFromEstablished(CallState.Establishing, CallState.Terminated));
        }

        [TestMethod]
        public void RecordReceivedFrame_IncrementsCounter()
        {
            var diagnostics = new MediaDiagnostics("call-id", useSpeechService: false);

            diagnostics.RecordReceivedFrame(640, 123);
            var frame = diagnostics.RecordReceivedFrame(640, 124);

            Assert.AreEqual(2, diagnostics.ReceivedFrames);
            Assert.AreEqual(2, frame.TotalFrames);
            Assert.AreEqual(640, frame.BufferLength);
            Assert.AreEqual(124, frame.Timestamp);
        }

        [TestMethod]
        public void ShouldLogFrame_LogsFirstAndIntervalOnly()
        {
            Assert.IsTrue(MediaDiagnostics.ShouldLogFrame(1));
            Assert.IsFalse(MediaDiagnostics.ShouldLogFrame(2));
            Assert.IsFalse(MediaDiagnostics.ShouldLogFrame(249));
            Assert.IsTrue(MediaDiagnostics.ShouldLogFrame(250));
            Assert.IsFalse(MediaDiagnostics.ShouldLogFrame(251));
            Assert.IsTrue(MediaDiagnostics.ShouldLogFrame(500));
        }

        [TestMethod]
        public void RecordSentFrame_IncrementsCounter()
        {
            var diagnostics = new MediaDiagnostics("call-id", useSpeechService: false);

            diagnostics.RecordSentFrame(640, 123);
            diagnostics.RecordSentFrame(640, 124);

            Assert.AreEqual(2, diagnostics.SentFrames);
        }

        [TestMethod]
        public void MediaMode_IsEchoWhenSpeechServiceIsDisabled()
        {
            var diagnostics = new MediaDiagnostics("call-id", useSpeechService: false);

            Assert.IsTrue(diagnostics.IsEchoMode);
            Assert.IsFalse(diagnostics.IsSpeechServiceMode);
            Assert.AreEqual("Echo", diagnostics.ModeName);
            Assert.AreEqual("Echo", CallDiagnostics.GetMediaMode(useSpeechService: false));
        }

        [TestMethod]
        public void MediaMode_IsSpeechServiceWhenEnabled()
        {
            var diagnostics = new MediaDiagnostics("call-id", useSpeechService: true);

            Assert.IsFalse(diagnostics.IsEchoMode);
            Assert.IsTrue(diagnostics.IsSpeechServiceMode);
            Assert.AreEqual("SpeechService", diagnostics.ModeName);
            Assert.AreEqual("SpeechService", CallDiagnostics.GetMediaMode(useSpeechService: true));
        }

        [TestMethod]
        public void DiagnosticFrame_DoesNotContainAudioPayload()
        {
            var diagnostics = new MediaDiagnostics("call-id", useSpeechService: false);

            var frame = diagnostics.RecordReceivedFrame(640, 123);

            Assert.AreEqual("call-id", frame.CallId);
            Assert.AreEqual(640, frame.BufferLength);
            Assert.AreEqual(123, frame.Timestamp);
        }

        [TestMethod]
        public void TryBeginShutdown_AllowsOnlyFirstRequest()
        {
            var diagnostics = new MediaDiagnostics("call-id", useSpeechService: false);

            Assert.IsTrue(diagnostics.TryBeginShutdown());
            Assert.IsFalse(diagnostics.TryBeginShutdown());
            Assert.IsFalse(diagnostics.TryBeginShutdown());
        }
    }
}
