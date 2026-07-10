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
    }
}
