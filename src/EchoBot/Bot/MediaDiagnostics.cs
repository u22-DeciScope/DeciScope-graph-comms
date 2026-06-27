namespace EchoBot.Bot
{
    public enum BotMediaMode
    {
        Echo,
        SpeechService,
    }

    public sealed class MediaDiagnostics
    {
        public const long FrameLogInterval = 250;

        private long receivedFrames;
        private long sentFrames;
        private int shutdownStarted;

        public MediaDiagnostics(string callId, bool useSpeechService)
        {
            CallId = callId ?? string.Empty;
            Mode = useSpeechService ? BotMediaMode.SpeechService : BotMediaMode.Echo;
        }

        public string CallId { get; }

        public BotMediaMode Mode { get; }

        public string ModeName => Mode.ToString();

        public long ReceivedFrames => Interlocked.Read(ref receivedFrames);

        public long SentFrames => Interlocked.Read(ref sentFrames);

        public bool IsEchoMode => Mode == BotMediaMode.Echo;

        public bool IsSpeechServiceMode => Mode == BotMediaMode.SpeechService;

        public MediaFrameDiagnostic RecordReceivedFrame(long bufferLength, long? timestamp)
        {
            var totalFrames = Interlocked.Increment(ref receivedFrames);
            return new MediaFrameDiagnostic(CallId, totalFrames, bufferLength, timestamp, ModeName, ShouldLogFrame(totalFrames));
        }

        public MediaFrameDiagnostic RecordSentFrame(long bufferLength, long? timestamp)
        {
            var totalFrames = Interlocked.Increment(ref sentFrames);
            return new MediaFrameDiagnostic(CallId, totalFrames, bufferLength, timestamp, ModeName, ShouldLogFrame(totalFrames));
        }

        public MediaFrameDiagnostic GetCurrentSentFrame(long bufferLength, long? timestamp)
        {
            var totalFrames = SentFrames;
            return new MediaFrameDiagnostic(CallId, totalFrames, bufferLength, timestamp, ModeName, ShouldLogFrame(totalFrames));
        }

        public bool TryBeginShutdown()
        {
            return Interlocked.CompareExchange(ref shutdownStarted, 1, 0) == 0;
        }

        public static bool ShouldLogFrame(long totalFrames)
        {
            return totalFrames == 1 || totalFrames % FrameLogInterval == 0;
        }
    }

    public sealed class MediaFrameDiagnostic
    {
        public MediaFrameDiagnostic(
            string callId,
            long totalFrames,
            long bufferLength,
            long? timestamp,
            string mediaMode,
            bool shouldLog)
        {
            CallId = callId;
            TotalFrames = totalFrames;
            BufferLength = bufferLength;
            Timestamp = timestamp;
            MediaMode = mediaMode;
            ShouldLog = shouldLog;
        }

        public string CallId { get; }

        public long TotalFrames { get; }

        public long BufferLength { get; }

        public long? Timestamp { get; }

        public string MediaMode { get; }

        public bool ShouldLog { get; }
    }
}
