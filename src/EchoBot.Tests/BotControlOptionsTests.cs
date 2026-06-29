using EchoBot.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EchoBot.Tests
{
    [TestClass]
    public class BotControlOptionsTests
    {
        [TestMethod]
        public void FromValues_DefaultsToAutoUserTrigger()
        {
            var options = BotControlOptions.FromValues(null, null, null);

            Assert.AreEqual(BotJoinMode.AutoUserTrigger, options.JoinMode);
            Assert.IsTrue(options.AutoUserTriggerEnabled);
            Assert.IsFalse(options.ControlApiEnabled);
        }

        [TestMethod]
        public void FromValues_CommandRequiresTokenForControlApi()
        {
            var withoutToken = BotControlOptions.FromValues("command", null, "http://0.0.0.0:7071");
            var withToken = BotControlOptions.FromValues("command", "secret", "http://0.0.0.0:7071");

            Assert.IsFalse(withoutToken.ControlApiEnabled);
            Assert.IsTrue(withToken.ControlApiEnabled);
            Assert.IsFalse(withToken.AutoUserTriggerEnabled);
        }

        [TestMethod]
        public void FromValues_BothEnablesCommandAndAutoUserTrigger()
        {
            var options = BotControlOptions.FromValues("both", "secret", null);

            Assert.IsTrue(options.ControlApiEnabled);
            Assert.IsTrue(options.AutoUserTriggerEnabled);
        }

        [TestMethod]
        public void FromValues_DisabledDisablesAllJoinModes()
        {
            var options = BotControlOptions.FromValues("disabled", "secret", null);

            Assert.IsFalse(options.ControlApiEnabled);
            Assert.IsFalse(options.AutoUserTriggerEnabled);
        }
    }
}
