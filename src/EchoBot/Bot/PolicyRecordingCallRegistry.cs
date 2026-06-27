using System.Collections.Concurrent;

namespace EchoBot.Bot
{
    public sealed class PolicyRecordingCallRegistry
    {
        private readonly ConcurrentDictionary<string, byte> callIds = new ConcurrentDictionary<string, byte>();

        public bool TryReserve(string? callId)
        {
            return !string.IsNullOrWhiteSpace(callId) && callIds.TryAdd(callId, 0);
        }

        public void Release(string? callId)
        {
            if (!string.IsNullOrWhiteSpace(callId))
            {
                callIds.TryRemove(callId, out _);
            }
        }
    }
}
