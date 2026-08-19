using Microsoft.Graph.Models;

namespace EchoBot.Bot
{
    public static class PolicyRecordingLifecycle
    {
        public static bool ShouldStartRecording(CallOrigin origin, CallState? oldState, CallState? newState)
        {
            return origin == CallOrigin.PolicyRecordingIncoming
                && CallDiagnostics.IsEstablishedTransition(oldState, newState);
        }

        public static bool CanPersistMediaOrDerivedData(
            CallOrigin origin,
            PolicyRecordingCallState state,
            bool terminationStarted)
        {
            return origin == CallOrigin.PolicyRecordingIncoming
                && state == PolicyRecordingCallState.RecordingStatusConfirmed
                && !terminationStarted;
        }
    }
}
