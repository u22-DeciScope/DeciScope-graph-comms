using System.Text.Json;
using EchoBot.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EchoBot.Tests
{
    [TestClass]
    public class BotMeetingStatusUpdateSerializationTests
    {
        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);

        [TestMethod]
        public void Serialize_IncludesDrainFieldsWhenProvided()
        {
            var update = new BotMeetingStatusUpdate(
                "ended",
                "call-1",
                "transcript queue drained",
                source: "bot_call_state",
                endReason: "manual_end_requested",
                lastFinalSequenceNo: 27,
                transcriptQueueDrained: true);

            var json = JsonSerializer.Serialize(update, JsonOptions);
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;

            Assert.AreEqual("ended", root.GetProperty("status").GetString());
            Assert.AreEqual(27, root.GetProperty("lastFinalSequenceNo").GetInt64());
            Assert.IsTrue(root.GetProperty("transcriptQueueDrained").GetBoolean());
        }

        [TestMethod]
        public void Serialize_OmitsDrainFieldsWhenNotProvided_ForOldApiCompatibility()
        {
            var update = new BotMeetingStatusUpdate("recording", "call-1", "recording started");

            var json = JsonSerializer.Serialize(update, JsonOptions);
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;

            Assert.IsFalse(root.TryGetProperty("lastFinalSequenceNo", out _), "未指定時は旧APIと同じ形を保つ");
            Assert.IsFalse(root.TryGetProperty("transcriptQueueDrained", out _));
        }

        [TestMethod]
        public void Serialize_KeepsDrainedFalseInsteadOfLying()
        {
            var update = new BotMeetingStatusUpdate(
                "ended",
                "call-1",
                "manual_end_requested (transcript_drain_timeout)",
                lastFinalSequenceNo: 24,
                transcriptQueueDrained: false);

            var json = JsonSerializer.Serialize(update, JsonOptions);
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;

            Assert.IsFalse(root.GetProperty("transcriptQueueDrained").GetBoolean());
            Assert.AreEqual(24, root.GetProperty("lastFinalSequenceNo").GetInt64());
        }
    }
}
