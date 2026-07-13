namespace EchoBot.Services
{
    /// <summary>
    /// Result of draining one session's transcript forwarding queue.
    /// </summary>
    /// <param name="Drained">
    /// True when every queued/in-flight forward for the session finished
    /// (including internal retries) before the wait was cancelled.
    /// </param>
    /// <param name="LastFinalSequenceNo">
    /// The highest final-transcript sequence number whose HTTP forward to the
    /// API actually succeeded, or null when no final transcript was forwarded
    /// successfully. Queued-but-unsent items never count.
    /// </param>
    /// <param name="PendingCount">Items still queued or in flight when the wait ended.</param>
    /// <param name="FailedCount">Forwards that terminally failed (after retries) for the session.</param>
    public sealed record TranscriptDrainResult(
        bool Drained,
        long? LastFinalSequenceNo,
        int PendingCount,
        int FailedCount);
}
