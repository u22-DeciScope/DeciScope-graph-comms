using Microsoft.Graph.Communications.Calls.Media;
using Microsoft.Skype.Bots.Media;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;

namespace EchoBot.Util
{
    /// <summary>
    /// The utility class.
    /// </summary>
    internal static class Utilities
    {
        public static List<AudioMediaBuffer> CreateAudioMediaBuffers(byte[] buffer, long currentTick, ILogger logger)
        {
            var audioMediaBuffers = new List<AudioMediaBuffer>();
            var referenceTime = currentTick;

            // packet size of 20 ms
            var numberOfTicksInOneAudioBuffers = 20 * 10000;

            // here we want to create buffers of 20MS with PCM 16Khz
            IntPtr unmanagedBuffer = Marshal.AllocHGlobal(640);
            Marshal.Copy(buffer, 0, unmanagedBuffer, 640);
            var audioBuffer = new AudioSendBuffer(unmanagedBuffer, 640, AudioFormat.Pcm16K, referenceTime);
            audioMediaBuffers.Add(audioBuffer);
            referenceTime += numberOfTicksInOneAudioBuffers;

            logger.LogTrace($"created {audioMediaBuffers.Count} AudioMediaBuffers");
            return audioMediaBuffers;
        }

        /// <summary>
        /// Helper to search the certificate store by its thumbprint.
        /// </summary>
        /// <returns>Certificate if found.</returns>
        /// <exception cref="Exception">No certificate with thumbprint {CertificateThumbprint} was found in the machine store.</exception>
        public static X509Certificate2 GetCertificateFromStore(string certificateThumbprint)
        {

            X509Store store = new X509Store(StoreName.My, StoreLocation.LocalMachine);
            store.Open(OpenFlags.ReadOnly);
            try
            {
                X509Certificate2Collection certs = store.Certificates.Find(X509FindType.FindByThumbprint, certificateThumbprint, validOnly: false);

                if (certs.Count != 1)
                {
                    throw new Exception($"No certificate with thumbprint {certificateThumbprint} was found in the machine store.");
                }

                return certs[0];
            }
            finally
            {
                store.Close();
            }
        }
    }
}

