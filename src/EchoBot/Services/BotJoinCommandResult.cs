namespace EchoBot.Services
{
    public sealed class BotJoinCommandResult
    {
        private BotJoinCommandResult(bool accepted, bool duplicate, string reason)
        {
            Accepted = accepted;
            Duplicate = duplicate;
            Reason = reason;
        }

        public bool Accepted { get; }

        public bool Duplicate { get; }

        public string Reason { get; }

        public static BotJoinCommandResult AcceptedNew() => new BotJoinCommandResult(true, false, "Accepted");

        public static BotJoinCommandResult AcceptedDuplicate() => new BotJoinCommandResult(true, true, "AlreadyProcessing");

        public static BotJoinCommandResult Rejected(string reason) => new BotJoinCommandResult(false, false, reason);
    }
}
