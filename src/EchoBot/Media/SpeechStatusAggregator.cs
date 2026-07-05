using EchoBot.Services;

namespace EchoBot.Media
{
    /// <summary>
    /// Aggregates the speech pipeline status (recording / speech_throttled / speech_error) reported by
    /// multiple independent <see cref="SpeechService"/> instances (one per unmixed speaker, plus the
    /// mixed-audio fallback instance) into a single session-wide status.
    /// </summary>
    /// <remarks>
    /// This class is intentionally pure/synchronous and side-effect free: it only tracks the latest
    /// status reported by each instance id and computes the aggregate. Callers decide whether and how
    /// to report the aggregate (e.g. via <see cref="IBotMeetingStatusReporter"/>), using the
    /// <see cref="SpeechStatusAggregatorResult.Changed"/> flag to avoid redundant reports.
    /// </remarks>
    public sealed class SpeechStatusAggregator
    {
        private readonly object gate = new object();
        private readonly Dictionary<string, string> statusesByInstanceId = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private string? lastNotifiedStatus;
        private bool hasNotified;

        /// <summary>
        /// Records the latest speech status for the given instance and recomputes the aggregate.
        /// </summary>
        /// <param name="instanceId">Stable identifier for the reporting instance (speaker id, or the mixed-fallback id).</param>
        /// <param name="status">One of <see cref="BotMeetingStatus.Recording"/>, <see cref="BotMeetingStatus.SpeechThrottled"/> or <see cref="BotMeetingStatus.SpeechError"/>.</param>
        public SpeechStatusAggregatorResult Update(string instanceId, string status)
        {
            if (string.IsNullOrWhiteSpace(instanceId))
            {
                throw new ArgumentException("Instance id must not be empty.", nameof(instanceId));
            }

            if (string.IsNullOrWhiteSpace(status))
            {
                throw new ArgumentException("Status must not be empty.", nameof(status));
            }

            lock (gate)
            {
                statusesByInstanceId[instanceId] = status;
                return Recompute();
            }
        }

        /// <summary>
        /// Removes the instance from the aggregate (e.g. when its <see cref="SpeechService"/> stops) and
        /// recomputes the aggregate over the remaining instances.
        /// </summary>
        public SpeechStatusAggregatorResult Remove(string instanceId)
        {
            if (string.IsNullOrWhiteSpace(instanceId))
            {
                throw new ArgumentException("Instance id must not be empty.", nameof(instanceId));
            }

            lock (gate)
            {
                statusesByInstanceId.Remove(instanceId);
                return Recompute();
            }
        }

        private SpeechStatusAggregatorResult Recompute()
        {
            var aggregated = ComputeAggregate();
            var changed = !hasNotified || !string.Equals(aggregated, lastNotifiedStatus, StringComparison.Ordinal);

            if (changed)
            {
                lastNotifiedStatus = aggregated;
                hasNotified = true;
            }

            return new SpeechStatusAggregatorResult(changed, aggregated);
        }

        private string? ComputeAggregate()
        {
            if (statusesByInstanceId.Count == 0)
            {
                return null;
            }

            var anyThrottled = false;
            foreach (var status in statusesByInstanceId.Values)
            {
                if (string.Equals(status, BotMeetingStatus.SpeechError, StringComparison.Ordinal))
                {
                    return BotMeetingStatus.SpeechError;
                }

                if (string.Equals(status, BotMeetingStatus.SpeechThrottled, StringComparison.Ordinal))
                {
                    anyThrottled = true;
                }
            }

            return anyThrottled ? BotMeetingStatus.SpeechThrottled : BotMeetingStatus.Recording;
        }
    }

    /// <summary>
    /// Result of a <see cref="SpeechStatusAggregator"/> update: the newly computed aggregate status and
    /// whether it differs from the last aggregate that was returned as "changed".
    /// </summary>
    public readonly struct SpeechStatusAggregatorResult
    {
        public SpeechStatusAggregatorResult(bool changed, string? aggregatedStatus)
        {
            Changed = changed;
            AggregatedStatus = aggregatedStatus;
        }

        /// <summary>
        /// True when <see cref="AggregatedStatus"/> differs from the aggregate produced by the previous
        /// call (or this is the very first call). Callers should only report status upstream when this
        /// is true, to avoid duplicate/flapping notifications.
        /// </summary>
        public bool Changed { get; }

        /// <summary>
        /// The current aggregate status, or null when no instance currently has a reported status.
        /// </summary>
        public string? AggregatedStatus { get; }
    }
}
