using MediaLogLevel = Microsoft.Skype.Bots.Media.LogLevel;

namespace EchoBot.Bot
{
    /// <summary>
    /// The MediaPlatformLogger.
    /// </summary>
    public class BotMediaLogger : IBotMediaLogger
    {
        /// <summary>
        /// The logger
        /// </summary>
        private readonly ILogger _logger;

        /// <summary>
        /// Detects audio socket receive stalls reported through the SDK's log statements.
        /// </summary>
        private readonly AudioSocketReceiveStallDetector _audioSocketReceiveStallDetector;

        /// <summary>
        /// Initializes a new instance of the <see cref="ExceptionLogger" /> class.
        /// </summary>
        /// <param name="logger">Graph logger.</param>
        /// <param name="audioSocketReceiveStallDetector">Detects audio socket receive stalls reported through SDK logs.</param>
        public BotMediaLogger(ILogger<BotMediaLogger> logger, AudioSocketReceiveStallDetector audioSocketReceiveStallDetector)
        {
            _logger = logger;
            _audioSocketReceiveStallDetector = audioSocketReceiveStallDetector;
        }

        public void WriteLog(MediaLogLevel level, string logStatement)
        {
            _audioSocketReceiveStallDetector.RecordFromLog(logStatement);

            if (ShouldSuppress(level, logStatement))
            {
                return;
            }

            LogLevel logLevel;
            switch (level)
            {
                case MediaLogLevel.Error:
                    logLevel = LogLevel.Error;
                    break;
                case MediaLogLevel.Warning:
                    logLevel = LogLevel.Warning;
                    break;
                case MediaLogLevel.Information:
                    logLevel = LogLevel.Information;
                    break;
                case MediaLogLevel.Verbose:
                    logLevel = LogLevel.Trace;
                    break;
                default:
                    logLevel = LogLevel.Trace;
                    break;
            }

            this._logger.Log(logLevel, logStatement);
        }

        internal static bool ShouldSuppress(MediaLogLevel level, string? logStatement)
        {
            if (level != MediaLogLevel.Information || string.IsNullOrWhiteSpace(logStatement))
            {
                return false;
            }

            return logStatement.Contains(
                "the audio player low on frames event was raised",
                StringComparison.OrdinalIgnoreCase);
        }
    }
}
