namespace EchoBot.Meetings
{
    public sealed class TeamsMeetingTitleResolutionRequest
    {
        public string? SessionId { get; init; }

        public string? TenantId { get; init; }

        public string? OriginalJoinUrl { get; init; }

        public string? ResolvedJoinUrl { get; init; }

        public string? JoinUrlHash { get; init; }

        public string? ThreadId { get; init; }

        public string? JoinMeetingId { get; init; }

        public string? OrganizerId { get; init; }

        public string? Stage { get; init; }
    }

    public sealed class TeamsMeetingTitleResolutionResult
    {
        public string? Title { get; init; }

        public string? TitleSource { get; init; }

        public string? Provider { get; init; } = "teams";

        public string? ExternalMeetingId { get; init; }

        public string? JoinMeetingId { get; init; }

        public string? JoinWebUrl { get; init; }

        public string? CanonicalJoinWebUrl { get; init; }

        public string? ThreadId { get; init; }

        public string? OrganizerId { get; init; }

        public string? OrganizerName { get; init; }

        public string? OrganizerEmail { get; init; }

        public DateTimeOffset? ScheduledStartAt { get; init; }

        public DateTimeOffset? ScheduledEndAt { get; init; }

        public string? ErrorCode { get; init; }

        public string? ErrorMessage { get; init; }

        public DateTimeOffset? TitleResolvedAt { get; init; }

        public bool HasTitle => !string.IsNullOrWhiteSpace(Title);
    }
}
