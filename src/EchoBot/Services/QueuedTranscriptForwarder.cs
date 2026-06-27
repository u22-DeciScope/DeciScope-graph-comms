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
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(segment);

            if (!options.Enabled)
            {
                return Task.FromResult(TranscriptForwardResult.Skipped());
            }

            if (channel.Writer.TryWrite(new TranscriptForwardWorkItem(segment, sequenceNo)))
            {
                return Task.FromResult(TranscriptForwardResult.QueuedForDelivery());
            }

            logger.LogWarning(
                "Transcript forward queue is full. Transcript was not queued. CallId={CallId}; SequenceNo={SequenceNo}; QueueCapacity={QueueCapacity}",
                segment.CallId,
                sequenceNo,
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
                await sender.ForwardAsync(item.Segment, item.SequenceNo, stoppingToken).ConfigureAwait(false);
            }
        }
    }
}
