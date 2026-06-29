namespace EchoBot.Services
{
    public interface ITranscriptSequenceProvider
    {
        Task<int> NextSequenceNoAsync(string callId, CancellationToken cancellationToken = default);
    }
}
