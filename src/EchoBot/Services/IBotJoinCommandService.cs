namespace EchoBot.Services
{
    public interface IBotJoinCommandService
    {
        BotJoinCommandResult TryEnqueue(string sessionId, string joinUrl, string? tenantId = null);
    }
}
