using EchoBot.Media;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EchoBot.Tests
{
    [TestClass]
    public class PcmAudioLevelTests
    {
        [TestMethod]
        public void Calculate_ReturnsZeroForSilence()
        {
            var level = PcmAudioLevelCalculator.Calculate(new byte[640]);

            Assert.AreEqual(0, level.PeakAmplitude);
            Assert.AreEqual(0, level.RmsAmplitude);
        }

        [TestMethod]
        public void Calculate_ReturnsPeakAmplitude()
        {
            var pcm = SamplesToBytes(1000, -2000, 3000);

            var level = PcmAudioLevelCalculator.Calculate(pcm);

            Assert.AreEqual(3000, level.PeakAmplitude);
        }

        [TestMethod]
        public void Calculate_ReturnsRmsAmplitude()
        {
            var pcm = SamplesToBytes(3, 4);

            var level = PcmAudioLevelCalculator.Calculate(pcm);

            Assert.AreEqual(Math.Sqrt(12.5), level.RmsAmplitude, 0.0001);
        }

        [TestMethod]
        public void Calculate_HandlesPositiveAndNegativeMaximumSamples()
        {
            var pcm = SamplesToBytes(short.MaxValue, short.MinValue);

            var level = PcmAudioLevelCalculator.Calculate(pcm);

            Assert.AreEqual(32768, level.PeakAmplitude);
            Assert.IsTrue(level.RmsAmplitude > 32767);
        }

        [TestMethod]
        public void Calculate_IgnoresTrailingOddByte()
        {
            var pcm = new byte[] { 0xE8, 0x03, 0x7F };

            var level = PcmAudioLevelCalculator.Calculate(pcm);

            Assert.AreEqual(1000, level.PeakAmplitude);
        }

        private static byte[] SamplesToBytes(params short[] samples)
        {
            var bytes = new byte[samples.Length * 2];
            for (var i = 0; i < samples.Length; i++)
            {
                bytes[i * 2] = (byte)(samples[i] & 0xFF);
                bytes[i * 2 + 1] = (byte)((samples[i] >> 8) & 0xFF);
            }

            return bytes;
        }
    }
}
