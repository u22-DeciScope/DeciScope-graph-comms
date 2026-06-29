namespace EchoBot.Services
{
    public interface IBotJoinCommandService
    {
        BotJoinCommandResult TryEnqueue(BotJoinCommand command);

        void MarkSessionEnded(string? sessionId, string? callId = null, string? reason = null);
    }
}
