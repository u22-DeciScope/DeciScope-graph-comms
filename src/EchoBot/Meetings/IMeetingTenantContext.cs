namespace EchoBot.Meetings
{
    public interface IMeetingTenantContext
    {
        string? MeetingTenantId { get; }

        IDisposable UseMeetingTenant(string meetingTenantId);
    }
}
