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
            long droppedFrames,
            string? recognizerInstanceIdHash = null,
            long pipelineGeneration = 0,
            DateTimeOffset? lastRecognizerStartedAtUtc = null,
            DateTimeOffset? lastSpeechPartialAtUtc = null,
            DateTimeOffset? lastSpeechFinalAtUtc = null)
        {
            ServiceAvailable = serviceAvailable;
            Started = started;
            AcceptingFrames = acceptingFrames;
            RecognizerCreated = recognizerCreated;
            PushStreamOpen = pushStreamOpen;
            DroppedFrames = droppedFrames;
            RecognizerInstanceIdHash = recognizerInstanceIdHash;
            PipelineGeneration = pipelineGeneration;
            LastRecognizerStartedAtUtc = lastRecognizerStartedAtUtc;
            LastSpeechPartialAtUtc = lastSpeechPartialAtUtc;
            LastSpeechFinalAtUtc = lastSpeechFinalAtUtc;
        }

        public bool ServiceAvailable { get; }

        public bool Started { get; }

        public bool AcceptingFrames { get; }

        public bool RecognizerCreated { get; }

        public bool PushStreamOpen { get; }

        public long DroppedFrames { get; }

        /// <summary>
        /// A one-way, shortened hash assigned to the recognizer object represented by this snapshot.
        /// The source identifier is never logged or sent in a heartbeat.
        /// </summary>
        public string? RecognizerInstanceIdHash { get; }

        public long PipelineGeneration { get; }

        public DateTimeOffset? LastRecognizerStartedAtUtc { get; }

        public DateTimeOffset? LastSpeechPartialAtUtc { get; }

        public DateTimeOffset? LastSpeechFinalAtUtc { get; }

        public bool Ready => ServiceAvailable && Started && AcceptingFrames && RecognizerCreated && PushStreamOpen;

        public static bool CallbackStateInvariantSatisfied(
            bool started,
            bool recognizerCreated,
            bool pushStreamOpen)
        {
            return started && recognizerCreated && pushStreamOpen;
        }

        public static SpeechPipelineSnapshot Unavailable() => new SpeechPipelineSnapshot(false, false, false, false, false, 0);

        /// <summary>
        /// Selects one coherent recognizer snapshot for session health. Flags are deliberately not ORed
        /// across recognizers because that could describe a pipeline which never actually existed.
        /// </summary>
        public static SpeechPipelineSnapshot Aggregate(IEnumerable<SpeechPipelineSnapshot> snapshots)
        {
            return snapshots
                .Where(snapshot => snapshot.ServiceAvailable)
                .OrderByDescending(snapshot => snapshot.Ready)
                .ThenByDescending(snapshot => snapshot.Started)
                .ThenByDescending(LatestActivityAtUtc)
                .ThenByDescending(snapshot => snapshot.PipelineGeneration)
                .FirstOrDefault()
                ?? Unavailable();
        }

        private static DateTimeOffset LatestActivityAtUtc(SpeechPipelineSnapshot snapshot)
        {
            return new[]
                {
                    snapshot.LastSpeechFinalAtUtc,
                    snapshot.LastSpeechPartialAtUtc,
                    snapshot.LastRecognizerStartedAtUtc,
                }
                .Where(value => value.HasValue)
                .Select(value => value!.Value)
                .DefaultIfEmpty(DateTimeOffset.MinValue)
                .Max();
        }
    }
}
