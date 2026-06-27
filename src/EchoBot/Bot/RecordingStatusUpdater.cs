using Microsoft.Graph.Communications.Calls;
using Microsoft.Graph.Models;

namespace EchoBot.Bot
{
    public sealed class RecordingStatusUpdater : IRecordingStatusUpdater
    {
        public Task UpdateRecordingStatusAsync(ICall call, RecordingStatus status, CancellationToken cancellationToken = default)
        {
            return call.UpdateRecordingStatusAsync(status, cancellationToken);
        }
    }
}
