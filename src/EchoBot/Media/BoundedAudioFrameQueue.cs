using System.Threading.Channels;

namespace EchoBot.Media
{
    public sealed class BoundedAudioFrameQueue
    {
        private readonly Channel<byte[]> channel;
        private long droppedFrames;

        public BoundedAudioFrameQueue(int capacity)
        {
            if (capacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity), "Audio queue capacity must be greater than zero.");
            }

            channel = Channel.CreateBounded<byte[]>(new BoundedChannelOptions(capacity)
            {
                SingleReader = true,
                SingleWriter = false,
                FullMode = BoundedChannelFullMode.Wait
            });
        }

        public ChannelReader<byte[]> Reader => channel.Reader;

        public long DroppedFrames => Interlocked.Read(ref droppedFrames);

        public bool TryEnqueue(byte[] frame)
        {
            if (frame == null)
            {
                throw new ArgumentNullException(nameof(frame));
            }

            if (channel.Writer.TryWrite(frame))
            {
                return true;
            }

            Interlocked.Increment(ref droppedFrames);
            return false;
        }

        public void Complete()
        {
            channel.Writer.TryComplete();
        }
    }
}
