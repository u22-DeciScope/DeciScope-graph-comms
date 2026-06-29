using System.Collections.Concurrent;

namespace EchoBot.Services
{
    public sealed class InMemoryTranscriptSequenceProvider : ITranscriptSequenceProvider
    {
        private readonly ConcurrentDictionary<string, int> sequenceNosByCallId = new ConcurrentDictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        public Task<int> NextSequenceNoAsync(string callId, CancellationToken cancellationToken = default)
        {
            var normalizedCallId = string.IsNullOrWhiteSpace(callId) ? "unknown" : callId;
            var sequenceNo = sequenceNosByCallId.AddOrUpdate(normalizedCallId, 1, (_, current) => current + 1);
            return Task.FromResult(sequenceNo);
        }
    }
}
