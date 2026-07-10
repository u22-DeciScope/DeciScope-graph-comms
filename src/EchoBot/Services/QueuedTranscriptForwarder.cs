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

        public override Task StopAsync(CancellationToken cancellationToken)
        {
            channel.Writer.TryComplete();
            return base.StopAsync(cancellationToken);
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            await foreach (var item in channel.Reader.ReadAllAsync().ConfigureAwait(false))
            {
                await sender.ForwardAsync(item.Segment, item.SequenceNo, item.IsFinal, stoppingToken).ConfigureAwait(false);
            }
        }
    }
}
