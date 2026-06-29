namespace EchoBot.Meetings
{
    public interface ITeamsMeetingTitleResolver
    {
        Task<TeamsMeetingTitleResolutionResult> ResolveAsync(
            TeamsMeetingTitleResolutionRequest request,
            CancellationToken cancellationToken);
    }
}
