using EchoBot.Models;

namespace EchoBot.Services
{
    public interface ITranscriptRepository
    {
        string DatabasePath { get; }

        Task InitializeAsync(CancellationToken cancellationToken = default);

        Task<int> SaveAsync(TranscriptSegment segment, CancellationToken cancellationToken = default);
    }
}
