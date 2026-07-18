using System.Collections.Concurrent;
using System.Threading.Channels;
using EchoBot.Models;

namespace EchoBot.Services
{
    public sealed class QueuedTranscriptForwarder : BackgroundService, ITranscriptForwarder
    {
        private readonly Channel<TranscriptForwardWorkItem> channel;
        private readonly TranscriptForwarder sender;
        private readonly TranscriptForwardingOptions options;
        private readonly ILogger<QueuedTranscriptForwarder> logger;

        // Per-session forwarding progress. Keyed by sessionId; items without a
        // sessionId are forwarded but not tracked (nothing can drain them).
        // The queue itself stays shared across every meeting - draining one
        // session never completes the channel or blocks other sessions.
        private readonly ConcurrentDictionary<string, SessionForwardingState> sessionStates =
            new ConcurrentDictionary<string, SessionForwardingState>(StringComparer.Ordinal);

        private sealed class SessionForwardingState
        {
            public int Pending;
            public int Failed;
            public long LastForwardedFinalSequenceNo;
            public readonly List<TaskCompletionSource<bool>> Waiters = new List<TaskCompletionSource<bool>>();
        }

        public QueuedTranscriptForwarder(
            TranscriptForwarder sender,
            TranscriptForwardingOptions options,
            ILogger<QueuedTranscriptForwarder> logger)
        {
            this.sender = sender;
            this.options = options;
            this.logger = logger;
            channel = Channel.CreateBounded<TranscriptForwardWorkItem>(
                new BoundedChannelOptions(options.QueueCapacity)
                {
                    FullMode = BoundedChannelFullMode.Wait,
                    SingleReader = true,
                    SingleWriter = false,
                });
        }

        public Task<TranscriptForwardResult> ForwardAsync(
            TranscriptSegment segment,
            int sequenceNo,
            bool isFinal = true,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(segment);

            if (!options.Enabled)
            {
                logger.LogInformation(
                    "Transcript forwarding skipped because forwarding is disabled. SessionId={SessionId}; CallId={CallId}; SequenceNo={SequenceNo}; TextLength={TextLength}; Reason={Reason}",
                    segment.SessionId,
                    segment.CallId,
                    sequenceNo,
                    segment.Text?.Length ?? 0,
                    options.Reason);
                return Task.FromResult(TranscriptForwardResult.Skipped());
            }

            if (string.IsNullOrWhiteSpace(segment.Text))
            {
                logger.LogInformation(
                    "Transcript forwarding skipped because transcript text is empty. SessionId={SessionId}; CallId={CallId}; SequenceNo={SequenceNo}; TextLength={TextLength}",
                    segment.SessionId,
                    segment.CallId,
                    sequenceNo,
                    segment.Text?.Length ?? 0);
                return Task.FromResult(TranscriptForwardResult.Skipped());
            }

            // Count the item as pending before it becomes visible to the
            // consumer, so a drain can never observe "queue readable but
            // pending == 0" and finish early.
            var state = TrackQueued(segment.SessionId);
            if (channel.Writer.TryWrite(new TranscriptForwardWorkItem(segment, sequenceNo, isFinal)))
            {
                logger.LogInformation(
                    "Transcript forwarding queued. SessionId={SessionId}; CallId={CallId}; SequenceNo={SequenceNo}; IsFinal={IsFinal}; ApiUrl={ApiUrl}; TextLength={TextLength}",
                    segment.SessionId,
                    segment.CallId,
                    sequenceNo,
                    isFinal,
                    options.ApiUrl,
                    segment.Text.Length);
                return Task.FromResult(TranscriptForwardResult.QueuedForDelivery());
            }

            if (state != null)
            {
                CompletePending(state, forwardedFinalSequenceNo: null, failed: true);
            }

            logger.LogWarning(
                "Transcript forwarding failed. SessionId={SessionId}; CallId={CallId}; SequenceNo={SequenceNo}; StatusCode={StatusCode}; ErrorMessage={ErrorMessage}; RetryAttempt={RetryAttempt}; QueueCapacity={QueueCapacity}",
                segment.SessionId,
                segment.CallId,
                sequenceNo,
                null,
                "Transcript forward queue is full.",
                0,
                options.QueueCapacity);
            return Task.FromResult(TranscriptForwardResult.Failed());
        }

        /// <inheritdoc />
        public async Task<TranscriptDrainResult> DrainSessionAsync(
            string? sessionId,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(sessionId) || !sessionStates.TryGetValue(sessionId, out var state))
            {
                // No transcript was ever queued for this session: nothing to
                // wait for, and no final sequence was forwarded.
                return new TranscriptDrainResult(true, null, 0, 0);
            }

            while (true)
            {
                TaskCompletionSource<bool> waiter;
                lock (state)
                {
                    if (state.Pending == 0)
                    {
                        return SnapshotLocked(state, drained: true);
                    }

                    waiter = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                    state.Waiters.Add(waiter);
                }

                using (cancellationToken.Register(() => waiter.TrySetResult(false)))
                {
                    await waiter.Task.ConfigureAwait(false);
                }

                if (cancellationToken.IsCancellationRequested)
                {
                    lock (state)
                    {
                        state.Waiters.Remove(waiter);
                        return SnapshotLocked(state, drained: state.Pending == 0);
                    }
                }
            }
        }

        private static TranscriptDrainResult SnapshotLocked(SessionForwardingState state, bool drained)
        {
            long last = state.LastForwardedFinalSequenceNo;
            return new TranscriptDrainResult(
                drained,
                last > 0 ? last : null,
                state.Pending,
                state.Failed);
        }

        private SessionForwardingState? TrackQueued(string? sessionId)
        {
            if (string.IsNullOrWhiteSpace(sessionId))
            {
                return null;
            }

            var state = sessionStates.GetOrAdd(sessionId, _ => new SessionForwardingState());
            lock (state)
            {
                state.Pending++;
            }
            return state;
        }

        private static void CompletePending(SessionForwardingState state, long? forwardedFinalSequenceNo, bool failed)
        {
            List<TaskCompletionSource<bool>>? toRelease = null;
            lock (state)
            {
                state.Pending--;
                if (failed)
                {
                    state.Failed++;
                }
                if (forwardedFinalSequenceNo.HasValue && forwardedFinalSequenceNo.Value > state.LastForwardedFinalSequenceNo)
                {
                    state.LastForwardedFinalSequenceNo = forwardedFinalSequenceNo.Value;
                }
                if (state.Pending <= 0 && state.Waiters.Count > 0)
                {
                    toRelease = new List<TaskCompletionSource<bool>>(state.Waiters);
                    state.Waiters.Clear();
                }
            }

            if (toRelease != null)
            {
                foreach (var waiter in toRelease)
                {
                    waiter.TrySetResult(true);
                }
            }
        }

        public override Task StopAsync(CancellationToken cancellationToken)
        {
            channel.Writer.TryComplete();
            return base.StopAsync(cancellationToken);
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            await foreach (var item in channel.Reader.ReadAllAsync().ConfigureAwait(false))
            {
                TranscriptForwardResult result;
                try
                {
                    result = await sender.ForwardAsync(item.Segment, item.SequenceNo, item.IsFinal, stoppingToken).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    // 送信側の想定外例外でconsumerループ自体を落とさない(落ちると
                    // 全セッションの転送が止まり、drainも永久に完了しなくなる)。
                    logger.LogError(
                        ex,
                        "Transcript forwarding worker caught unexpected sender exception. SessionId={SessionId}; CallId={CallId}; SequenceNo={SequenceNo}",
                        item.Segment.SessionId,
                        item.Segment.CallId,
                        item.SequenceNo);
                    result = TranscriptForwardResult.Failed();
                }

                var sessionId = item.Segment.SessionId;
                if (!string.IsNullOrWhiteSpace(sessionId) && sessionStates.TryGetValue(sessionId, out var state))
                {
                    var forwardedFinal = item.IsFinal && result.Attempted && result.Success
                        ? item.SequenceNo
                        : (long?)null;
                    // Skipped (not attempted) items complete the pending count but
                    // never advance the forwarded sequence and are not failures.
                    var failed = result.Attempted && !result.Success;
                    CompletePending(state, forwardedFinal, failed);
                }
            }
        }
    }
}
