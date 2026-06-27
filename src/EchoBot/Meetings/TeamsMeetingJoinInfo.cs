using Microsoft.Graph.Models;

namespace EchoBot.Meetings
{
    public sealed class TeamsMeetingJoinInfo
    {
        public TeamsMeetingJoinInfo(
            ChatInfo chatInfo,
            MeetingInfo meetingInfo,
            string? tenantId,
            Uri resolvedJoinUrl,
            bool redirected,
            bool defaultTenantIdUsed = false)
        {
            ChatInfo = chatInfo;
            MeetingInfo = meetingInfo;
            TenantId = tenantId;
            ResolvedJoinUrl = resolvedJoinUrl;
            Redirected = redirected;
            DefaultTenantIdUsed = defaultTenantIdUsed;
        }

        public ChatInfo ChatInfo { get; }

        public MeetingInfo MeetingInfo { get; }

        public string? TenantId { get; }

        public Uri ResolvedJoinUrl { get; }

        public bool Redirected { get; }

        public bool DefaultTenantIdUsed { get; }
    }
}
