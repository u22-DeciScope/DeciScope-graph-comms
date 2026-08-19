using EchoBot.Services;

namespace EchoBot.Bot
{
    /// <summary>
    /// Arms a one-shot deadline from the audio-frame boundary. Silent audio
    /// frames count as transport activity, so normal meeting silence never
    /// produces a receive-stall event.
    /// </summary>
    public sealed class AudioReceiveLivenessMonitor : IDisposable
    {
        public static readonly TimeSpan DefaultStallThreshold = TimeSpan.FromSeconds(5);

        private readonly object sync = new object();
        private readonly object publishSync = new object();
        private readonly string callId;
        private readonly TimeSpan stallThreshold;
        private readonly Func<DateTimeOffset> utcNow;
        private readonly Func<BotMediaHealthUpdate, Task> publish;
        private readonly Timer timer;
        private DateTimeOffset lastFrameAtUtc;
        private DateTimeOffset stallStartedAtUtc;
        private string activeEventId = string.Empty;
        private long eventSequence;
        private bool armed;
        private bool stalled;
        private bool disposed;
        private Task publishTail = Task.CompletedTask;

        public AudioReceiveLivenessMonitor(
            string callId,
            Func<BotMediaHealthUpdate, Task> publish,
            TimeSpan? stallThreshold = null,
            Func<DateTimeOffset>? utcNow = null)
        {
            this.callId = callId;
            this.publish = publish ?? throw new ArgumentNullException(nameof(publish));
            this.stallThreshold = stallThreshold ?? DefaultStallThreshold;
            this.utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
            timer = new Timer(OnDeadline, null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        }

        public void ObserveFrame(DateTimeOffset? observedAtUtc = null)
        {
            BotMediaHealthUpdate? recovered = null;
            lock (sync)
            {
                if (disposed)
                {
                    return;
                }

                var observed = (observedAtUtc ?? utcNow()).ToUniversalTime();
                lastFrameAtUtc = observed;
                armed = true;
                if (stalled)
                {
                    var duration = Math.Max(0, (long)(observed - stallStartedAtUtc).TotalMilliseconds);
                    recovered = new BotMediaHealthUpdate(
                        activeEventId + ":recovered",
                        "ok",
                        "recovered",
                        observed,
                        stallStartedAtUtc,
                        observed,
                        duration);
                    stalled = false;
                    activeEventId = string.Empty;
                }
                timer.Change(stallThreshold, Timeout.InfiniteTimeSpan);
            }

            if (recovered != null)
            {
                EnqueuePublish(recovered);
            }
        }

        private void OnDeadline(object? _)
        {
            CheckDeadline(utcNow().ToUniversalTime());
        }

        internal void CheckDeadline(DateTimeOffset checkedAtUtc)
        {
            BotMediaHealthUpdate? started = null;
            lock (sync)
            {
                if (disposed || !armed || stalled)
                {
                    return;
                }
                var now = checkedAtUtc.ToUniversalTime();
                var elapsed = now - lastFrameAtUtc;
                if (elapsed < stallThreshold)
                {
                    timer.Change(stallThreshold - elapsed, Timeout.InfiniteTimeSpan);
                    return;
                }

                stalled = true;
                stallStartedAtUtc = lastFrameAtUtc;
                activeEventId = $"audio-receive-stall:{callId}:{Interlocked.Increment(ref eventSequence)}";
                started = new BotMediaHealthUpdate(
                    activeEventId + ":started",
                    "audio_receive_stalled",
                    "started",
                    now,
                    stallStartedAtUtc,
                    lastFrameAtUtc,
                    Math.Max(0, (long)elapsed.TotalMilliseconds));
            }

            EnqueuePublish(started);
        }

        private void EnqueuePublish(BotMediaHealthUpdate update)
        {
            lock (publishSync)
            {
                // Media callbacks remain non-blocking, while the forwarding
                // chain preserves started -> recovered ordering even when the
                // first HTTP request is still in flight at recovery time.
                publishTail = publishTail.ContinueWith(
                    _ => PublishSafelyAsync(update),
                    CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default).Unwrap();
            }
        }

        internal Task WaitForPendingPublicationsAsync()
        {
            lock (publishSync)
            {
                return publishTail;
            }
        }

        private async Task PublishSafelyAsync(BotMediaHealthUpdate update)
        {
            try
            {
                await publish(update).ConfigureAwait(false);
            }
            catch
            {
                // The caller's reporter owns structured forwarding-failure
                // logging. Transport health must never throw into media SDK
                // callbacks or recreate/end the recognizer/call.
            }
        }

        public void Dispose()
        {
            lock (sync)
            {
                if (disposed)
                {
                    return;
                }
                disposed = true;
                armed = false;
                timer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            }
            timer.Dispose();
        }
    }
}
