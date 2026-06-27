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

        public static string GetMediaMode(bool useSpeechService)
        {
            return useSpeechService ? BotMediaMode.SpeechService.ToString() : BotMediaMode.Echo.ToString();
        }
    }
}
