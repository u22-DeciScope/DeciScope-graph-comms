namespace EchoBot.Bot
{
    /// <summary>
    /// Detects "Detected a stall on Audio socket ... direction: Receive" log lines that the
    /// Microsoft.Skype.Bots.Media SDK reports through <see cref="BotMediaLogger"/>, and records the most
    /// recent occurrence.
    /// The SDK does not expose a structured event for this condition, only a log line. Depending on the
    /// SDK's log text is not ideal, but there is no alternative today; if the SDK changes this wording, this
    /// detection silently stops working (it will not throw, it will just stop recording stalls).
    /// State here is process-wide (not scoped to a single call/session), which matches this bot's
    /// single-meeting-per-process deployment model.
    /// </summary>
    public sealed class AudioSocketReceiveStallDetector
    {
        private long lastStallAtUtcTicks;
        private long stallCount;

        /// <summary>
        /// Returns true when <paramref name="logStatement"/> looks like an SDK "Detected a stall on Audio
        /// socket" log line for the Receive direction.
        /// </summary>
        public static bool IsReceiveStallLog(string? logStatement)
        {
            return !string.IsNullOrEmpty(logStatement)
                && logStatement.IndexOf("Detected a stall on Audio socket", StringComparison.OrdinalIgnoreCase) >= 0
                && logStatement.IndexOf("direction: Receive", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>
        /// Records a receive stall if <paramref name="logStatement"/> matches <see cref="IsReceiveStallLog"/>.
        /// No-op otherwise.
        /// </summary>
        public void RecordFromLog(string? logStatement)
        {
            if (!IsReceiveStallLog(logStatement))
            {
                return;
            }

            Interlocked.Exchange(ref lastStallAtUtcTicks, DateTime.UtcNow.Ticks);
            Interlocked.Increment(ref stallCount);
        }

        /// <summary>
        /// UTC time of the most recently recorded receive stall, or null if none has been recorded.
        /// </summary>
        public DateTimeOffset? LastReceiveStallAtUtc
        {
            get
            {
                var ticks = Interlocked.Read(ref lastStallAtUtcTicks);
                return ticks == 0 ? (DateTimeOffset?)null : new DateTimeOffset(ticks, TimeSpan.Zero);
            }
        }

        /// <summary>
        /// Total number of receive stalls recorded since this detector was created.
        /// </summary>
        public long ReceiveStallCount => Interlocked.Read(ref stallCount);
    }
}
