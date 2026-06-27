using EchoBot.Media;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EchoBot.Tests
{
    [TestClass]
    public class BoundedAudioFrameQueueTests
    {
        [TestMethod]
        public async Task TryEnqueue_AddsFrameToReader()
        {
            var queue = new BoundedAudioFrameQueue(1);
            var frame = new byte[] { 1, 2, 3 };

            var accepted = queue.TryEnqueue(frame);
            var read = await queue.Reader.ReadAsync();

            Assert.IsTrue(accepted);
            CollectionAssert.AreEqual(frame, read);
        }

        [TestMethod]
        public void TryEnqueue_WhenFull_DropsFrame()
        {
            var queue = new BoundedAudioFrameQueue(1);

            Assert.IsTrue(queue.TryEnqueue(new byte[] { 1 }));
            Assert.IsFalse(queue.TryEnqueue(new byte[] { 2 }));
            Assert.AreEqual(1, queue.DroppedFrames);
        }

        [TestMethod]
        public void Complete_CompletesReader()
        {
            var queue = new BoundedAudioFrameQueue(1);

            queue.Complete();

            Assert.IsTrue(queue.Reader.Completion.IsCompleted);
        }

        [TestMethod]
        public void Constructor_RejectsInvalidCapacity()
        {
            Assert.ThrowsException<ArgumentOutOfRangeException>(() => new BoundedAudioFrameQueue(0));
        }
    }
}
