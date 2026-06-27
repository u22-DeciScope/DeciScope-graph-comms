namespace EchoBot.Media
{
    public readonly struct PcmAudioLevel
    {
        public PcmAudioLevel(int peakAmplitude, double rmsAmplitude)
        {
            PeakAmplitude = peakAmplitude;
            RmsAmplitude = rmsAmplitude;
        }

        public int PeakAmplitude { get; }

        public double RmsAmplitude { get; }
    }

    public static class PcmAudioLevelCalculator
    {
        public static PcmAudioLevel Calculate(ReadOnlySpan<byte> pcm16LittleEndian)
        {
            var sampleCount = pcm16LittleEndian.Length / 2;
            if (sampleCount == 0)
            {
                return new PcmAudioLevel(0, 0);
            }

            var peak = 0;
            double sumSquares = 0;
            for (var i = 0; i < sampleCount; i++)
            {
                var sample = (short)(pcm16LittleEndian[i * 2] | (pcm16LittleEndian[i * 2 + 1] << 8));
                var amplitude = sample == short.MinValue ? 32768 : Math.Abs(sample);
                if (amplitude > peak)
                {
                    peak = amplitude;
                }

                sumSquares += (double)sample * sample;
            }

            return new PcmAudioLevel(peak, Math.Sqrt(sumSquares / sampleCount));
        }
    }
}
