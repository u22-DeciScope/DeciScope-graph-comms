using Microsoft.Graph.Communications.Calls;
using Microsoft.Graph.Models;

namespace EchoBot.Bot
{
    public interface IRecordingStatusUpdater
    {
        Task UpdateRecordingStatusAsync(ICall call, RecordingStatus status, CancellationToken cancellationToken = default);
    }
}
