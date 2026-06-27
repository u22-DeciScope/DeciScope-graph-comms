namespace EchoBot.Media
{
    public sealed class SpeechPipelineSnapshot
    {
        public SpeechPipelineSnapshot(
            bool serviceAvailable,
            bool started,
            bool acceptingFrames,
            bool recognizerCreated,
            bool pushStreamOpen,
            long droppedFrames)
        {
            ServiceAvailable = serviceAvailable;
            Started = started;
            AcceptingFrames = acceptingFrames;
            RecognizerCreated = recognizerCreated;
            PushStreamOpen = pushStreamOpen;
            DroppedFrames = droppedFrames;
        }

        public bool ServiceAvailable { get; }

        public bool Started { get; }

        public bool AcceptingFrames { get; }

        public bool RecognizerCreated { get; }

        public bool PushStreamOpen { get; }

        public long DroppedFrames { get; }

        public bool Ready => ServiceAvailable && Started && AcceptingFrames && RecognizerCreated && PushStreamOpen;

        public static SpeechPipelineSnapshot Unavailable() => new SpeechPipelineSnapshot(false, false, false, false, false, 0);
    }
}
