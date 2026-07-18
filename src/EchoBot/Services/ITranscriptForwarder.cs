using EchoBot.Models;

namespace EchoBot.Services
{
    public interface ITranscriptForwarder
    {
        Task<TranscriptForwardResult> ForwardAsync(
            TranscriptSegment segment,
            int sequenceNo,
            bool isFinal = true,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Waits until every queued/in-flight forward for the given session has
        /// finished (including internal retries), without stopping forwarding
        /// for any other session. Cancelling the token stops waiting and
        /// returns the current (non-drained) state instead of throwing.
        /// </summary>
        Task<TranscriptDrainResult> DrainSessionAsync(
            string? sessionId,
            CancellationToken cancellationToken = default);
    }
}
