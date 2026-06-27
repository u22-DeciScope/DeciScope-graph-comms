namespace EchoBot.Meetings
{
    public sealed class MeetingJoinOptions
    {
        public const string DefaultTenantIdEnvironmentVariable = "DECISCOPE_DEFAULT_TENANT_ID";

        private MeetingJoinOptions(string? defaultTenantId)
        {
            DefaultTenantId = defaultTenantId;
        }

        public string? DefaultTenantId { get; }

        public bool DefaultTenantIdConfigured => !string.IsNullOrWhiteSpace(DefaultTenantId);

        public static MeetingJoinOptions FromEnvironment()
        {
            return FromValues(Environment.GetEnvironmentVariable(DefaultTenantIdEnvironmentVariable));
        }

        public static MeetingJoinOptions FromValues(string? defaultTenantId)
        {
            return new MeetingJoinOptions(string.IsNullOrWhiteSpace(defaultTenantId) ? null : defaultTenantId.Trim());
        }
    }
}
