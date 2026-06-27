using System.Threading;

namespace EchoBot.Meetings
{
    public sealed class MeetingTenantContext : IMeetingTenantContext
    {
        private readonly AsyncLocal<string?> _meetingTenantId = new();

        public string? MeetingTenantId => _meetingTenantId.Value;

        public IDisposable UseMeetingTenant(string meetingTenantId)
        {
            var previousTenantId = _meetingTenantId.Value;
            _meetingTenantId.Value = meetingTenantId;
            return new Scope(this, previousTenantId);
        }

        private sealed class Scope : IDisposable
        {
            private readonly MeetingTenantContext _context;
            private readonly string? _previousTenantId;
            private bool _disposed;

            public Scope(MeetingTenantContext context, string? previousTenantId)
            {
                _context = context;
                _previousTenantId = previousTenantId;
            }

            public void Dispose()
            {
                if (_disposed)
                {
                    return;
                }

                _context._meetingTenantId.Value = _previousTenantId;
                _disposed = true;
            }
        }
    }
}
