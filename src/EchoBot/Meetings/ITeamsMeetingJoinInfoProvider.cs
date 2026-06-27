using EchoBot.Models;

namespace EchoBot.Meetings
{
    public interface ITeamsMeetingJoinInfoProvider
    {
        Task<TeamsMeetingJoinInfo> GetJoinInfoAsync(JoinCallBody joinCallBody, CancellationToken cancellationToken);
    }
}
