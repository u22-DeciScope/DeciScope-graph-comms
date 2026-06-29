using EchoBot.Models;

namespace EchoBot.Services
{
    public sealed class TranscriptForwardWorkItem
    {
        public TranscriptForwardWorkItem(TranscriptSegment segment, int sequenceNo, bool isFinal)
        {
            Segment = segment;
            SequenceNo = sequenceNo;
            IsFinal = isFinal;
        }

        public TranscriptSegment Segment { get; }

        public int SequenceNo { get; }

        public bool IsFinal { get; }
    }
}
