using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
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

        public BotJoinCommandResult TryEnqueue(BotJoinCommand command)
        {
            var sessionId = command?.SessionId;
            var joinUrl = command?.JoinUrl;
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
                logger.LogInformation(
                    "Duplicate join ignored. SessionId={SessionId}; MeetingUrlHash={MeetingUrlHash}; CandidateUserIdsCount={CandidateUserIdsCount}; Reason=SessionAlreadyJoiningOrActive",
                    sessionId,
                    HashForLog(joinUrl),
                    command?.CandidateUserIds?.Count ?? 0);
                return BotJoinCommandResult.AcceptedDuplicate();
            }

            var candidateUserIds = CandidateUserIds(command);
            logger.LogInformation(
                "Meeting title lookup candidates received. SessionId={SessionId}; CandidateUserIdsCount={CandidateUserIdsCount}; CandidateUserIdsHash={CandidateUserIdsHash}; JoinMeetingId={JoinMeetingId}; CreatedByMicrosoftUserIdHash={CreatedByMicrosoftUserIdHash}; CreatedByEmailHash={CreatedByEmailHash}",
                sessionId,
                candidateUserIds.Count,
                HashesForLog(candidateUserIds),
                command?.JoinMeetingId,
                HashForLog(command?.CreatedByMicrosoftUserId),
                HashForLog(command?.CreatedByEmail));

            if (!queue.Writer.TryWrite(new QueuedJoinCommand(
                sessionId,
                joinUrl,
                command?.TenantId,
                candidateUserIds,
                command?.CreatedByMicrosoftUserId,
                command?.CreatedByEmail,
                command?.JoinMeetingId,
                command?.CanonicalJoinWebUrl)))
            {
                activeSessions.TryRemove(sessionId, out _);
                return BotJoinCommandResult.Rejected("join command queue is unavailable.");
            }

            return BotJoinCommandResult.AcceptedNew();
        }

        public void MarkSessionEnded(string? sessionId, string? callId = null, string? reason = null)
        {
            if (string.IsNullOrWhiteSpace(sessionId))
            {
                return;
            }

            var removed = activeSessions.TryRemove(sessionId, out _);
            logger.LogInformation(
                "Join session marked ended. SessionId={SessionId}; CallId={CallId}; Removed={Removed}; Reason={Reason}",
                sessionId,
                callId,
                removed,
                reason);
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
                logger.LogInformation(
                    "Join started. SessionId={SessionId}; MeetingUrlHash={MeetingUrlHash}; CandidateUserIdsCount={CandidateUserIdsCount}; JoinMeetingId={JoinMeetingId}",
                    command.SessionId,
                    HashForLog(command.JoinUrl),
                    command.CandidateUserIds.Count,
                    command.JoinMeetingId);
                await statusReporter.ReportAsync(
                    command.SessionId,
                    BotMeetingStatus.Joining,
                    "join command accepted",
                    cancellationToken: cancellationToken).ConfigureAwait(false);

                using var scope = serviceProvider.CreateScope();
                var botService = scope.ServiceProvider.GetRequiredService<IBotService>();
                var call = await botService.JoinMeetingAsync(command.SessionId, command.JoinUrl, command.TenantId, command.CandidateUserIds, command.JoinMeetingId, command.CanonicalJoinWebUrl, cancellationToken).ConfigureAwait(false);

                logger.LogInformation(
                    "Join succeeded. SessionId={SessionId}; MeetingUrlHash={MeetingUrlHash}; CallId={CallId}",
                    command.SessionId,
                    HashForLog(command.JoinUrl),
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
                    "Join failed. SessionId={SessionId}; MeetingUrlHash={MeetingUrlHash}; ExceptionType={ExceptionType}",
                    command.SessionId,
                    HashForLog(command.JoinUrl),
                    ex.GetType().Name);
                await statusReporter.ReportAsync(
                    command.SessionId,
                    BotMeetingStatus.Failed,
                    GetFailureMessage(ex),
                    cancellationToken: CancellationToken.None,
                    failedReason: GetFailureReason(ex),
                    errorCode: ex.GetType().Name,
                    source: "graph_join").ConfigureAwait(false);
                activeSessions.TryRemove(command.SessionId, out _);
            }
        }

        private static string GetFailureMessage(Exception exception)
        {
            return exception is EchoBot.Meetings.TeamsMeetingJoinException
                ? exception.Message
                : "failed to join meeting";
        }

        private static string GetFailureReason(Exception exception)
        {
            return exception is EchoBot.Meetings.TeamsMeetingJoinException joinException
                ? joinException.Code
                : "graph_join_failed";
        }

        private static IReadOnlyCollection<string> CandidateUserIds(BotJoinCommand? command)
        {
            var values = new List<string>();
            if (!string.IsNullOrWhiteSpace(command?.CreatedByMicrosoftUserId))
            {
                values.Add(command.CreatedByMicrosoftUserId);
            }
            if (!string.IsNullOrWhiteSpace(command?.CreatedByEmail))
            {
                values.Add(command.CreatedByEmail);
            }
            if (command?.CandidateUserIds != null)
            {
                values.AddRange(command.CandidateUserIds);
            }
            return UniqueTrimmed(values);
        }

        private static IReadOnlyCollection<string> UniqueTrimmed(IEnumerable<string> values)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var result = new List<string>();
            foreach (var value in values)
            {
                var trimmed = value?.Trim();
                if (string.IsNullOrWhiteSpace(trimmed) || !seen.Add(trimmed))
                {
                    continue;
                }
                result.Add(trimmed);
            }
            return result;
        }

        private static string HashForLog(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }
            var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value.Trim()));
            return Convert.ToHexString(bytes, 0, 8);
        }

        private static string HashesForLog(IEnumerable<string>? values)
        {
            if (values == null)
            {
                return "[]";
            }

            var hashes = values
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => HashForLog(value))
                .ToArray();
            return hashes.Length == 0 ? "[]" : $"[{string.Join(",", hashes)}]";
        }

        private sealed class QueuedJoinCommand
        {
            public QueuedJoinCommand(
                string sessionId,
                string joinUrl,
                string? tenantId,
                IReadOnlyCollection<string> candidateUserIds,
                string? createdByMicrosoftUserId,
                string? createdByEmail,
                string? joinMeetingId,
                string? canonicalJoinWebUrl)
            {
                SessionId = sessionId;
                JoinUrl = joinUrl;
                TenantId = tenantId;
                CandidateUserIds = candidateUserIds;
                CreatedByMicrosoftUserId = createdByMicrosoftUserId;
                CreatedByEmail = createdByEmail;
                JoinMeetingId = joinMeetingId;
                CanonicalJoinWebUrl = canonicalJoinWebUrl;
            }

            public string SessionId { get; }

            public string JoinUrl { get; }

            public string? TenantId { get; }

            public IReadOnlyCollection<string> CandidateUserIds { get; }

            public string? CreatedByMicrosoftUserId { get; }

            public string? CreatedByEmail { get; }

            public string? JoinMeetingId { get; }

            public string? CanonicalJoinWebUrl { get; }
        }
    }
}
