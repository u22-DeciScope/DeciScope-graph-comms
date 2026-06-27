using EchoBot.Models;

namespace EchoBot.Services
{
    public sealed class TranscriptForwardWorkItem
    {
        public TranscriptForwardWorkItem(TranscriptSegment segment, int sequenceNo)
        {
            Segment = segment;
            SequenceNo = sequenceNo;
        }

        public TranscriptSegment Segment { get; }

        public int SequenceNo { get; }
    }
}
