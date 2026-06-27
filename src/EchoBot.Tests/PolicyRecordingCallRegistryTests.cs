using EchoBot.Bot;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EchoBot.Tests
{
    [TestClass]
    public class PolicyRecordingCallRegistryTests
    {
        [TestMethod]
        public void TryReserve_AllowsCallIdOnlyOnce()
        {
            var registry = new PolicyRecordingCallRegistry();

            Assert.IsTrue(registry.TryReserve("call-id"));
            Assert.IsFalse(registry.TryReserve("call-id"));
        }

        [TestMethod]
        public void TryReserve_RejectsEmptyCallId()
        {
            var registry = new PolicyRecordingCallRegistry();

            Assert.IsFalse(registry.TryReserve(null));
            Assert.IsFalse(registry.TryReserve(string.Empty));
            Assert.IsFalse(registry.TryReserve(" "));
        }

        [TestMethod]
        public void Release_AllowsCallIdToBeReservedAgain()
        {
            var registry = new PolicyRecordingCallRegistry();

            Assert.IsTrue(registry.TryReserve("call-id"));
            registry.Release("call-id");

            Assert.IsTrue(registry.TryReserve("call-id"));
        }
    }
}
