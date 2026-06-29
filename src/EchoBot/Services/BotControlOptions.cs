namespace EchoBot.Services
{
    public sealed class BotControlOptions
    {
        public const string JoinModeEnvironmentVariable = "DECISCOPE_BOT_JOIN_MODE";
        public const string ControlTokenEnvironmentVariable = "DECISCOPE_BOT_CONTROL_TOKEN";
        public const string ControlBindUrlEnvironmentVariable = "DECISCOPE_BOT_CONTROL_BIND_URL";

        public const string DefaultJoinModeValue = "auto_user_trigger";

        private BotControlOptions(BotJoinMode joinMode, string? controlToken, string? bindUrl, string reason)
        {
            JoinMode = joinMode;
            ControlToken = controlToken;
            BindUrl = bindUrl;
            Reason = reason;
        }

        public BotJoinMode JoinMode { get; }

        public string? ControlToken { get; }

        public string? BindUrl { get; }

        public string Reason { get; }

        public bool ControlApiEnabled =>
            (JoinMode == BotJoinMode.Command || JoinMode == BotJoinMode.Both)
            && !string.IsNullOrWhiteSpace(ControlToken);

        public bool AutoUserTriggerEnabled =>
            JoinMode == BotJoinMode.AutoUserTrigger || JoinMode == BotJoinMode.Both;

        public static BotControlOptions FromEnvironment()
        {
            return FromValues(
                Environment.GetEnvironmentVariable(JoinModeEnvironmentVariable),
                Environment.GetEnvironmentVariable(ControlTokenEnvironmentVariable),
                Environment.GetEnvironmentVariable(ControlBindUrlEnvironmentVariable));
        }

        public static BotControlOptions FromValues(string? joinModeValue, string? controlToken, string? bindUrl)
        {
            var modeValue = string.IsNullOrWhiteSpace(joinModeValue)
                ? DefaultJoinModeValue
                : joinModeValue.Trim();

            var mode = ParseJoinMode(modeValue, out var parsed)
                ? parsed
                : BotJoinMode.AutoUserTrigger;

            var reason = ParseJoinMode(modeValue, out _)
                ? "Configured"
                : "InvalidJoinModeDefaultedToAutoUserTrigger";

            return new BotControlOptions(
                mode,
                string.IsNullOrWhiteSpace(controlToken) ? null : controlToken,
                string.IsNullOrWhiteSpace(bindUrl) ? null : bindUrl.Trim(),
                reason);
        }

        private static bool ParseJoinMode(string value, out BotJoinMode mode)
        {
            switch (value.Trim().ToLowerInvariant())
            {
                case "auto_user_trigger":
                    mode = BotJoinMode.AutoUserTrigger;
                    return true;
                case "command":
                    mode = BotJoinMode.Command;
                    return true;
                case "both":
                    mode = BotJoinMode.Both;
                    return true;
                case "disabled":
                    mode = BotJoinMode.Disabled;
                    return true;
                default:
                    mode = BotJoinMode.AutoUserTrigger;
                    return false;
            }
        }
    }
}
