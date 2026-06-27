using System.Net;

namespace EchoBot.Services
{
    public sealed class TranscriptForwardResult
    {
        private TranscriptForwardResult(bool attempted, bool success, HttpStatusCode? statusCode, bool duplicate, bool queued)
        {
            Attempted = attempted;
            Success = success;
            StatusCode = statusCode;
            Duplicate = duplicate;
            Queued = queued;
        }

        public bool Attempted { get; }

        public bool Success { get; }

        public HttpStatusCode? StatusCode { get; }

        public bool Duplicate { get; }

        public bool Queued { get; }

        public static TranscriptForwardResult Skipped() => new TranscriptForwardResult(false, false, null, false, false);

        public static TranscriptForwardResult QueuedForDelivery() => new TranscriptForwardResult(false, true, null, false, true);

        public static TranscriptForwardResult Succeeded(HttpStatusCode statusCode, bool duplicate) =>
            new TranscriptForwardResult(true, true, statusCode, duplicate, false);

        public static TranscriptForwardResult Failed(HttpStatusCode? statusCode = null) =>
            new TranscriptForwardResult(true, false, statusCode, false, false);
    }
}
