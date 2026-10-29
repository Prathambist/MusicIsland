using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;
using NAudio.Wave;

namespace MusicIsland
{
    /// <summary>
    /// Captures whatever is playing on the default output device (WASAPI
    /// loopback), runs an FFT and exposes normalised bass → treble band levels.
    /// </summary>
    public sealed class AudioVisualizer : IDisposable, IMMNotificationClient
    {
        const int FftSize = 2048;

        // Log-spaced band edges in Hz: sub-bass, bass, low-mid, mid, upper-mid, presence, brilliance
        static readonly double[] BandEdges = { 35, 80, 180, 400, 900, 2000, 4500, 12000 };

        public static int BandCount => BandEdges.Length - 1;

        readonly object sync = new();
        readonly float[] ring = new float[FftSize];
        readonly float[] window = new float[FftSize];
        readonly double[] re = new double[FftSize];
        readonly double[] im = new double[FftSize];
        readonly float[] levels = new float[BandCount];
        readonly double[] bandPeaks = new double[BandCount];

        readonly MMDeviceEnumerator enumerator = new();

        WasapiLoopbackCapture? capture;
        int ringPos;
        double globalPeak = 20;
        DateTime lastProcess = DateTime.UtcNow;
        bool disposed;

        public bool IsRunning => capture != null;

        public DateTime LastSound { get; private set; } = DateTime.MinValue;

        public AudioVisualizer()
        {
            for (int i = 0; i < FftSize; i++)
                window[i] = (float)(0.5 - 0.5 * Math.Cos(2 * Math.PI * i / (FftSize - 1)));

            for (int i = 0; i < BandCount; i++)
                bandPeaks[i] = 20;
        }

        public bool Start()
        {
            try
            {
                enumerator.RegisterEndpointNotificationCallback(this);
            }
            catch
            {
            }

            return StartCapture();
        }

        readonly object captureLock = new();

        bool StartCapture()
        {
            lock (captureLock)
                return StartCaptureLocked();
        }

        bool StartCaptureLocked()
        {
            StopCapture();

            if (disposed)
                return false;

            try
            {
                var c = new WasapiLoopbackCapture();
                c.DataAvailable += OnData;
                c.RecordingStopped += (s, e) =>
                {
                    // Device unplugged / exclusive-mode app took over: retry shortly
                    if (!disposed && e.Exception != null)
                        Task.Delay(1500).ContinueWith(_ => StartCapture());
                };
                c.StartRecording();
                capture = c;
                return true;
            }
            catch
            {
                capture = null;
                return false;
            }
        }

        void StopCapture()
        {
            var c = capture;
            capture = null;

            if (c == null)
                return;

            try
            {
                c.DataAvailable -= OnData;
                c.StopRecording();
                c.Dispose();
            }
            catch
            {
            }
        }

        public void GetLevels(float[] destination)
        {
            lock (sync)
            {
                // No packets arrive while the device is silent; let bars fall
                if ((DateTime.UtcNow - lastProcess).TotalMilliseconds > 150)
                    Array.Clear(levels);

                Array.Copy(levels, destination, Math.Min(levels.Length, destination.Length));
            }
        }

        void OnData(object? sender, WaveInEventArgs e)
        {
            var format = capture?.WaveFormat;

            if (format == null || e.BytesRecorded == 0)
                return;

            int channels = format.Channels;
            int bytesPerSample = format.BitsPerSample / 8;
            int frameBytes = bytesPerSample * channels;
            // The shared-mode mix format is 32-bit float, usually wrapped as "Extensible"
            bool isFloat = format.Encoding == WaveFormatEncoding.IeeeFloat ||
                           (format.Encoding == WaveFormatEncoding.Extensible && format.BitsPerSample == 32);

            for (int offset = 0; offset + frameBytes <= e.BytesRecorded; offset += frameBytes)
            {
                float sum = 0;

                for (int ch = 0; ch < channels; ch++)
                {
                    int p = offset + ch * bytesPerSample;

                    sum += isFloat && bytesPerSample == 4
                        ? BitConverter.ToSingle(e.Buffer, p)
                        : bytesPerSample == 2
                            ? BitConverter.ToInt16(e.Buffer, p) / 32768f
                            : bytesPerSample == 4
                                ? BitConverter.ToInt32(e.Buffer, p) / 2147483648f
                                : 0;
                }

                ring[ringPos] = sum / channels;
                ringPos = (ringPos + 1) % FftSize;
            }

            Analyse(format.SampleRate);
        }

        void Analyse(int sampleRate)
        {
            for (int i = 0; i < FftSize; i++)
            {
                re[i] = ring[(ringPos + i) % FftSize] * window[i];
                im[i] = 0;
            }

            Fft(re, im);

            var now = DateTime.UtcNow;
            double dt = Math.Clamp((now - lastProcess).TotalSeconds, 0, 0.25);
            double binHz = (double)sampleRate / FftSize;
            var bandDb = new double[BandCount];
            double loudest = double.MinValue;

            for (int b = 0; b < BandCount; b++)
            {
                int lo = Math.Max(1, (int)(BandEdges[b] / binHz));
                int hi = Math.Min(FftSize / 2, Math.Max(lo + 1, (int)(BandEdges[b + 1] / binHz)));
                double power = 0;

                for (int k = lo; k < hi; k++)
                    power += re[k] * re[k] + im[k] * im[k];

                power /= hi - lo;
                bandDb[b] = 10 * Math.Log10(power + 1e-9);
                loudest = Math.Max(loudest, bandDb[b]);
            }

            // Automatic gain: peaks decay slowly so quiet and loud songs both fill the bars.
            // A full-scale sine with a Hann window is ~54 dB here; the floor stops
            // amplifying background hiss when nothing is playing.
            const double decayPerSecond = 9;
            const double range = 34;

            globalPeak = Math.Max(Math.Max(globalPeak - decayPerSecond * dt, loudest), 4);

            if (loudest > -20)
                LastSound = now;

            lock (sync)
            {
                for (int b = 0; b < BandCount; b++)
                {
                    bandPeaks[b] = Math.Max(
                        Math.Max(bandPeaks[b] - decayPerSecond * dt, bandDb[b]),
                        globalPeak - 18);

                    double level = (bandDb[b] - (bandPeaks[b] - range)) / range;
                    level = Math.Clamp(level, 0, 1);
                    levels[b] = (float)Math.Pow(level, 1.4);
                }

                lastProcess = now;
            }
        }

        // In-place iterative radix-2 Cooley–Tukey FFT
        static void Fft(double[] re, double[] im)
        {
            int n = re.Length;

            for (int i = 1, j = 0; i < n; i++)
            {
                int bit = n >> 1;

                for (; (j & bit) != 0; bit >>= 1)
                    j ^= bit;

                j ^= bit;

                if (i < j)
                {
                    (re[i], re[j]) = (re[j], re[i]);
                    (im[i], im[j]) = (im[j], im[i]);
                }
            }

            for (int len = 2; len <= n; len <<= 1)
            {
                double angle = -2 * Math.PI / len;
                double wRe = Math.Cos(angle);
                double wIm = Math.Sin(angle);

                for (int i = 0; i < n; i += len)
                {
                    double curRe = 1, curIm = 0;

                    for (int k = 0; k < len / 2; k++)
                    {
                        int a = i + k;
                        int b = a + len / 2;
                        double tRe = re[b] * curRe - im[b] * curIm;
                        double tIm = re[b] * curIm + im[b] * curRe;

                        re[b] = re[a] - tRe;
                        im[b] = im[a] - tIm;
                        re[a] += tRe;
                        im[a] += tIm;

                        double next = curRe * wRe - curIm * wIm;
                        curIm = curRe * wIm + curIm * wRe;
                        curRe = next;
                    }
                }
            }
        }

        public void Dispose()
        {
            disposed = true;

            try
            {
                enumerator.UnregisterEndpointNotificationCallback(this);
            }
            catch
            {
            }

            StopCapture();
            enumerator.Dispose();
        }

        // Follow the user when they switch speakers / headphones
        void IMMNotificationClient.OnDefaultDeviceChanged(DataFlow flow, Role role, string defaultDeviceId)
        {
            if (flow == DataFlow.Render && role == Role.Multimedia)
                Task.Delay(300).ContinueWith(_ => StartCapture());
        }

        void IMMNotificationClient.OnDeviceStateChanged(string deviceId, DeviceState newState) { }
        void IMMNotificationClient.OnDeviceAdded(string pwstrDeviceId) { }
        void IMMNotificationClient.OnDeviceRemoved(string deviceId) { }
        void IMMNotificationClient.OnPropertyValueChanged(string pwstrDeviceId, PropertyKey key) { }
    }
}
