namespace EchoBot.Meetings
{
    public static class MeetingTenantValidator
    {
        public static string NormalizeAndValidate(string? tenantId, string applicationId)
        {
            if (string.IsNullOrWhiteSpace(tenantId))
            {
                throw new TeamsMeetingJoinException("missing_tenant_id", "tenantId is required for this Teams meeting URL.");
            }

            if (!Guid.TryParse(tenantId.Trim(), out var tenantGuid))
            {
                throw new TeamsMeetingJoinException("invalid_tenant_id", "tenantId must be a valid Microsoft Entra directory (tenant) ID.");
            }

            if (Guid.TryParse(applicationId, out var applicationGuid) && tenantGuid == applicationGuid)
            {
                throw new TeamsMeetingJoinException("invalid_tenant_id", "tenantId must be the Microsoft Entra directory (tenant) ID, not the application (client) ID.");
            }

            return tenantGuid.ToString("D");
        }

        public static string Suffix(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "none";
            }

            var trimmed = value.Trim();
            return trimmed.Length <= 8 ? trimmed : trimmed[^8..];
        }
    }
}
