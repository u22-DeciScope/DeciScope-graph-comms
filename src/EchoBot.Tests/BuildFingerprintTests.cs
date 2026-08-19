using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EchoBot.Tests
{
    [TestClass]
    public class BuildFingerprintTests
    {
        [TestMethod]
        public void Current_ExposesCompleteNonSecretBuildIdentity()
        {
            var fingerprint = BuildFingerprint.Current;

            Assert.AreEqual("DeciScope-graph-comms", fingerprint.RepositoryName);
            Assert.IsFalse(string.IsNullOrWhiteSpace(fingerprint.AssemblyVersion));
            Assert.IsFalse(string.IsNullOrWhiteSpace(fingerprint.InformationalVersion));
            Assert.IsFalse(string.IsNullOrWhiteSpace(fingerprint.BuildVersion));
            Assert.IsFalse(string.IsNullOrWhiteSpace(fingerprint.GitCommitSha));
            Assert.IsFalse(string.IsNullOrWhiteSpace(fingerprint.BuildTimestamp));
            Assert.IsFalse(string.IsNullOrWhiteSpace(fingerprint.DirtyBuild));
            Assert.IsFalse(string.IsNullOrWhiteSpace(fingerprint.RuntimeEnvironment));
        }
    }
}
