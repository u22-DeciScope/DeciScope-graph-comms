using System.Runtime.InteropServices;
using EchoBot.Bot;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EchoBot.Tests
{
    [TestClass]
    public class AudioFrameCopyTests
    {
        [TestMethod]
        public void CopyPcmFromPointer_CopiesBytes()
        {
            var source = new byte[] { 1, 2, 3, 4 };
            var pointer = Marshal.AllocHGlobal(source.Length);
            try
            {
                Marshal.Copy(source, 0, pointer, source.Length);

                var copy = BotMediaStream.CopyPcmFromPointer(pointer, source.Length);

                CollectionAssert.AreEqual(source, copy);
            }
            finally
            {
                Marshal.FreeHGlobal(pointer);
            }
        }

        [TestMethod]
        public void CopyPcmFromPointer_DoesNotDependOnOriginalMemoryAfterCopy()
        {
            var source = new byte[] { 1, 2, 3, 4 };
            var pointer = Marshal.AllocHGlobal(source.Length);
            try
            {
                Marshal.Copy(source, 0, pointer, source.Length);
                var copy = BotMediaStream.CopyPcmFromPointer(pointer, source.Length);
                Marshal.WriteByte(pointer, 0, 9);

                CollectionAssert.AreEqual(source, copy);
            }
            finally
            {
                Marshal.FreeHGlobal(pointer);
            }
        }
    }
}
