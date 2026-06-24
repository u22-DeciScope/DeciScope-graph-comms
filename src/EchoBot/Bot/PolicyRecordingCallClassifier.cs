using Microsoft.Graph.Models;

namespace EchoBot.Bot
{
    public static class PolicyRecordingCallClassifier
    {
        public static bool IsPolicyRecordingIncoming(Call? call)
        {
            if (call?.IncomingContext == null)
            {
                return false;
            }

            return call.Direction == CallDirection.Incoming || call.Direction == null;
        }

        public static bool ShouldWarnAnswerLatency(long elapsedMs, int warningThresholdMs)
        {
            var threshold = warningThresholdMs > 0 ? warningThresholdMs : 3000;
            return elapsedMs >= threshold;
        }
    }
}
