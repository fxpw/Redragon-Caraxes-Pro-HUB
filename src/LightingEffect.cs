using System;
using System.Diagnostics;
using System.IO;
using NAudio.Dsp;
using NAudio.Wave;

namespace Caraxes
{
    internal sealed class LightingEffect : IDisposable
    {
        readonly Stopwatch time = Stopwatch.StartNew();
        readonly SpectrumCapture audio;
        internal readonly string Mode;

        internal LightingEffect(string mode)
        {
            if (mode != "wave" && mode != "breathe" && mode != "comet" && mode != "aurora" && mode != "spectrum")
                throw new InvalidDataException("Unknown lighting mode.");
            Mode = mode;
            if (mode == "spectrum") audio = new SpectrumCapture();
        }

        internal byte[] Frame()
        {
            double seconds = time.Elapsed.TotalSeconds;
            var levels = audio == null ? null : audio.Levels();
            return CanvasFrame.Render((column, row) => {
                switch (Mode)
                {
                    case "wave": return Color(column / 15.0 - seconds / 4.0 + row * .018, .64);
                    case "breathe": return Color(.73, .05 + .65 * (.5 - .5 * Math.Cos(seconds * Math.PI / 2)));
                    case "comet":
                        double head = seconds * 7 % 22 - 4;
                        double distance = head - column - row * .26;
                        double trail = distance >= 0 ? Math.Exp(-distance * .7) : Math.Exp(distance * 3);
                        return Color(.56 + row * .023, .025 + trail * .8);
                    case "aurora":
                        double ribbon = 2 + Math.Sin(column * .39 - seconds * 1.25) * 1.45;
                        double glow = Math.Exp(-Math.Abs(row - ribbon) * .85);
                        return Color(.46 + .30 * (.5 + .5 * Math.Sin(column * .25 + seconds * .7)), .04 + glow * .7);
                    default:
                        int band = Math.Min(levels.Length - 1, column * levels.Length / 14);
                        double height = Math.Max(0, Math.Min(1, levels[band] * 5 - (4 - row)));
                        return Color(.39 - (4 - row) * .065 + band * .013, height * .8);
                }
            });
        }

        static int Color(double hue, double brightness)
        {
            double position = 6 * (hue - Math.Floor(hue));
            int sector = (int)position;
            int high = (int)(brightness * 255);
            int up = (int)(high * (position - sector));
            int down = high - up;
            switch (sector)
            {
                case 0: return high << 16 | up << 8;
                case 1: return down << 16 | high << 8;
                case 2: return high << 8 | up;
                case 3: return down << 8 | high;
                case 4: return up << 16 | high;
                default: return high << 16 | down;
            }
        }

        public void Dispose() { if (audio != null) audio.Dispose(); }
    }

    internal sealed class SpectrumCapture : IDisposable
    {
        const int SampleRate = 48000;
        const int FftBits = 11;
        static readonly int[] Edges = { 40, 85, 130, 200, 310, 480, 730, 1100, 1700, 2600, 4100, 6500, 12000 };
        readonly WasapiLoopbackCapture capture = new WasapiLoopbackCapture();
        readonly Complex[] samples = new Complex[1 << FftBits];
        readonly float[] window = new float[1 << FftBits];
        readonly double[] bands = new double[Edges.Length - 1];
        int sample;
        long lastData;
        Exception stopped;

        internal SpectrumCapture()
        {
            try
            {
                for (int index = 0; index < window.Length; index++) window[index] = (float)FastFourierTransform.HannWindow(index, window.Length);
                capture.WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(SampleRate, 2);
                capture.DataAvailable += Data;
                capture.RecordingStopped += (sender, args) => { lock (bands) stopped = args.Exception; };
                capture.StartRecording();
            }
            catch { capture.Dispose(); throw; }
        }

        void Data(object sender, WaveInEventArgs args)
        {
            for (int offset = 0; offset + 7 < args.BytesRecorded; offset += 8)
            {
                float left = BitConverter.ToSingle(args.Buffer, offset);
                float right = BitConverter.ToSingle(args.Buffer, offset + 4);
                samples[sample].X = (left + right) * .5f * window[sample];
                samples[sample].Y = 0;
                if (++sample != samples.Length) continue;
                sample = 0;
                FastFourierTransform.FFT(true, FftBits, samples);
                lock (bands)
                {
                    for (int index = 0; index < bands.Length; index++)
                    {
                        int first = Math.Max(1, (int)Math.Ceiling(Edges[index] * samples.Length / (double)SampleRate));
                        int last = Math.Max(first + 1, (int)Math.Ceiling(Edges[index + 1] * samples.Length / (double)SampleRate));
                        double peak = 0;
                        for (int bin = first; bin < last; bin++)
                            peak = Math.Max(peak, samples[bin].X * samples[bin].X + samples[bin].Y * samples[bin].Y);
                        double level = Math.Max(0, Math.Min(1, (10 * Math.Log10(Math.Max(peak, 1e-12)) + 61) / 46));
                        bands[index] += (level - bands[index]) * (level > bands[index] ? .67 : .23);
                    }
                    lastData = Stopwatch.GetTimestamp();
                }
            }
        }

        internal double[] Levels()
        {
            lock (bands)
            {
                if (stopped != null) throw new IOException("Windows audio capture stopped.", stopped);
                var result = new double[bands.Length];
                double elapsed = (Stopwatch.GetTimestamp() - lastData) / (double)Stopwatch.Frequency;
                double decay = Math.Exp(-8 * Math.Max(0, elapsed - .08));
                for (int index = 0; index < bands.Length; index++) result[index] = bands[index] * decay;
                return result;
            }
        }

        public void Dispose() { capture.Dispose(); }
    }
}
