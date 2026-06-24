namespace EchoBot.Meetings
{
    public class TeamsMeetingJoinException : Exception
    {
        public TeamsMeetingJoinException(string code, string message)
            : base(message)
        {
            Code = code;
        }

        public TeamsMeetingJoinException(string code, string message, Exception innerException)
            : base(message, innerException)
        {
            Code = code;
        }

        public string Code { get; }
    }
}
