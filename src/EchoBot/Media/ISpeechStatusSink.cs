namespace EchoBot.Media
{
    /// <summary>
    /// Receives per-instance speech pipeline status updates (recording / speech_throttled / speech_error)
    /// from a <see cref="SpeechService"/> so that a session-level owner (e.g. BotMediaStream) can
    /// aggregate the status across every concurrently running SpeechService instance (one per unmixed
    /// speaker, plus the mixed-audio fallback instance) before deciding whether to report it upstream.
    /// </summary>
    public interface ISpeechStatusSink
    {
        /// <summary>
        /// Reports that <paramref name="instanceId"/>'s speech status is now <paramref name="status"/>.
        /// </summary>
        Task ReportAsync(string instanceId, string status, string message, string? failedReason, string? errorCode);

        /// <summary>
        /// Removes <paramref name="instanceId"/> from the aggregate, typically called once from
        /// <see cref="SpeechService.StopAsync"/> as the instance shuts down.
        /// </summary>
        Task RemoveAsync(string instanceId);
    }
}
