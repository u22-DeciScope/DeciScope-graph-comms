using Microsoft.Graph.Models;

namespace EchoBot.Bot
{
    public static class CallDiagnostics
    {
        public static bool IsEstablishedTransition(CallState? oldState, CallState? newState)
        {
            return oldState != newState && newState == CallState.Established;
        }

        public static bool IsTerminatedFromEstablished(CallState? oldState, CallState? newState)
        {
            return oldState == CallState.Established && newState == CallState.Terminated;
        }

        /// <summary>
        /// True when the call has reached the terminal <see cref="CallState.Terminated"/> state, regardless
        /// of which state it transitioned from. Unlike <see cref="IsTerminatedFromEstablished"/>, this also
        /// covers calls that end via an intermediate <see cref="CallState.Terminating"/> notification (a
        /// separate OnUpdated event with oldState=Terminating) - the common path when Teams/the organizer
        /// ends the call rather than the bot calling ICall.DeleteAsync() itself - and calls that never
        /// reached Established before failing/ending.
        /// </summary>
        public static bool IsTerminated(CallState? oldState, CallState? newState)
        {
            return newState == CallState.Terminated && oldState != CallState.Terminated;
        }

        public static string GetMediaMode(bool useSpeechService)
        {
            return useSpeechService ? BotMediaMode.SpeechService.ToString() : BotMediaMode.Echo.ToString();
        }
    }
}
