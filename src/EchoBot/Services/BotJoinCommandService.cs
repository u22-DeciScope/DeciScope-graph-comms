using System.Collections.Concurrent;
using System.Threading.Channels;
using EchoBot.Bot;
using EchoBot.Models;

namespace EchoBot.Services
{
    public sealed class BotJoinCommandService : BackgroundService, IBotJoinCommandService
    {
        private readonly Channel<QueuedJoinCommand> queue = Channel.CreateUnbounded<QueuedJoinCommand>();
        private readonly ConcurrentDictionary<string, byte> activeSessions = new ConcurrentDictionary<string, byte>(StringComparer.OrdinalIgnoreCase);
        private readonly IServiceProvider serviceProvider;
        private readonly IBotMeetingStatusReporter statusReporter;
        private readonly ILogger<BotJoinCommandService> logger;

        public BotJoinCommandService(
            IServiceProvider serviceProvider,
            IBotMeetingStatusReporter statusReporter,
            ILogger<BotJoinCommandService> logger)
        {
            this.serviceProvider = serviceProvider;
            this.statusReporter = statusReporter;
            this.logger = logger;
        }

        public BotJoinCommandResult TryEnqueue(string sessionId, string joinUrl, string? tenantId = null)
        {
            if (string.IsNullOrWhiteSpace(sessionId))
            {
                return BotJoinCommandResult.Rejected("sessionId is required.");
            }

            if (string.IsNullOrWhiteSpace(joinUrl))
            {
                return BotJoinCommandResult.Rejected("joinUrl is required.");
            }

            if (!activeSessions.TryAdd(sessionId, 0))
            {
                return BotJoinCommandResult.AcceptedDuplicate();
            }

            if (!queue.Writer.TryWrite(new QueuedJoinCommand(sessionId, joinUrl, tenantId)))
            {
                activeSessions.TryRemove(sessionId, out _);
                return BotJoinCommandResult.Rejected("join command queue is unavailable.");
            }

            return BotJoinCommandResult.AcceptedNew();
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            await foreach (var command in queue.Reader.ReadAllAsync(stoppingToken).ConfigureAwait(false))
            {
                _ = ProcessCommandAsync(command, stoppingToken);
            }
        }

        private async Task ProcessCommandAsync(QueuedJoinCommand command, CancellationToken cancellationToken)
        {
            try
            {
                logger.LogInformation("Processing bot join command. SessionId={SessionId}", command.SessionId);
                await statusReporter.ReportAsync(
                    command.SessionId,
                    BotMeetingStatus.Joining,
                    "join command accepted",
                    cancellationToken: cancellationToken).ConfigureAwait(false);

                using var scope = serviceProvider.CreateScope();
                var botService = scope.ServiceProvider.GetRequiredService<IBotService>();
                var call = await botService.JoinMeetingAsync(command.SessionId, command.JoinUrl, command.TenantId, cancellationToken).ConfigureAwait(false);

                logger.LogInformation(
                    "Bot joined meeting. SessionId={SessionId}; BotCallId={BotCallId}",
                    command.SessionId,
                    call.Id);

                await statusReporter.ReportAsync(
                    command.SessionId,
                    BotMeetingStatus.Joined,
                    "joined successfully",
                    call.Id,
                    cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                logger.LogError(
                    ex,
                    "Bot join command failed. SessionId={SessionId}; ExceptionType={ExceptionType}",
                    command.SessionId,
                    ex.GetType().Name);
                await statusReporter.ReportAsync(
                    command.SessionId,
                    BotMeetingStatus.Failed,
                    GetFailureMessage(ex),
                    cancellationToken: CancellationToken.None).ConfigureAwait(false);
                activeSessions.TryRemove(command.SessionId, out _);
            }
        }

        private static string GetFailureMessage(Exception exception)
        {
            return exception is EchoBot.Meetings.TeamsMeetingJoinException
                ? exception.Message
                : "failed to join meeting";
        }

        private sealed class QueuedJoinCommand
        {
            public QueuedJoinCommand(string sessionId, string joinUrl, string? tenantId)
            {
                SessionId = sessionId;
                JoinUrl = joinUrl;
                TenantId = tenantId;
            }

            public string SessionId { get; }

            public string JoinUrl { get; }

            public string? TenantId { get; }
        }
    }
}
