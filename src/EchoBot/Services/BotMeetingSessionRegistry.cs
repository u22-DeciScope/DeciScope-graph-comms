using System.Collections.Concurrent;

namespace EchoBot.Services
{
    public sealed class BotMeetingSessionRegistry
    {
        private readonly ConcurrentDictionary<string, string> sessionByCallKey = new ConcurrentDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<string, string> callIdBySession = new ConcurrentDictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public void Register(string callKey, string sessionId, bool primaryCallId = true)
        {
            if (string.IsNullOrWhiteSpace(callKey) || string.IsNullOrWhiteSpace(sessionId))
            {
                return;
            }

            sessionByCallKey[callKey] = sessionId;
            if (primaryCallId)
            {
                callIdBySession[sessionId] = callKey;
            }
        }

        public string? GetSessionId(string? callKey)
        {
            if (string.IsNullOrWhiteSpace(callKey))
            {
                return null;
            }

            return sessionByCallKey.TryGetValue(callKey, out var sessionId)
                ? sessionId
                : null;
        }

        public void Remove(string? callKey)
        {
            if (!string.IsNullOrWhiteSpace(callKey))
            {
                if (sessionByCallKey.TryRemove(callKey, out var sessionId))
                {
                    if (callIdBySession.TryGetValue(sessionId, out var mappedCallId)
                        && string.Equals(mappedCallId, callKey, StringComparison.OrdinalIgnoreCase))
                    {
                        callIdBySession.TryRemove(sessionId, out _);
                    }
                }
            }
        }

        public string? GetCallId(string? sessionId)
        {
            if (string.IsNullOrWhiteSpace(sessionId))
            {
                return null;
            }

            return callIdBySession.TryGetValue(sessionId, out var callId)
                ? callId
                : null;
        }
    }
}
