using System.Net;

namespace EchoBot.Services
{
    public sealed class TranscriptForwardResult
    {
        private TranscriptForwardResult(bool attempted, bool success, HttpStatusCode? statusCode, bool duplicate)
        {
            Attempted = attempted;
            Success = success;
            StatusCode = statusCode;
            Duplicate = duplicate;
        }

        public bool Attempted { get; }

        public bool Success { get; }

        public HttpStatusCode? StatusCode { get; }

        public bool Duplicate { get; }

        public static TranscriptForwardResult Skipped() => new TranscriptForwardResult(false, false, null, false);

        public static TranscriptForwardResult Succeeded(HttpStatusCode statusCode, bool duplicate) =>
            new TranscriptForwardResult(true, true, statusCode, duplicate);

        public static TranscriptForwardResult Failed(HttpStatusCode? statusCode = null) =>
            new TranscriptForwardResult(true, false, statusCode, false);
    }
}
