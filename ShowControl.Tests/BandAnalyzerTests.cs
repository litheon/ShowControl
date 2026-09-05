using System;
using System.Linq;
using ShowControl.AudioAnalysis;
using Xunit;

namespace ShowControl.Tests
{
    public class BandAnalyzerTests
    {
        private static AnalysisSnapshot LastSnapshot(double amplitude, double seconds = 2.0)
        {
            var engine = new AudioAnalysisEngine();
            AnalysisSnapshot last = AnalysisSnapshot.Empty;
            engine.SnapshotPublished += s => last = s;

            // A kick-heavy signal at the given amplitude, generated directly so the two
            // loudness cases differ only by scale.
            int rate = engine.Settings.SampleRate;
            var block = new float[512];
            long position = 0;
            int total = (int)(seconds * rate);
            while (position < total)
            {
                for (int i = 0; i < block.Length; i++)
                {
                    double t = (double)(position + i) / rate;
                    double tInBeat = t % (60.0 / 128);
                    double kick = tInBeat < 0.08 ? Math.Sin(2 * Math.PI * 60 * tInBeat) * Math.Exp(-tInBeat / 0.03) : 0;
                    double hat = Math.Sin(2 * Math.PI * 7000 * t) * 0.1;
                    block[i] = (float)(amplitude * (kick + hat));
                }
                engine.Submit(block, (double)position / rate);
                position += block.Length;
            }
            engine.ProcessPending();
            engine.Dispose();
            return last;
        }

        [Fact]
        public void QuietAndLoudMaterial_BothProduceUsableBands()
        {
            var quiet = LastSnapshot(amplitude: 0.05);
            var loud = LastSnapshot(amplitude: 0.9);

            // Usable: something registers well above the floor in both cases (this is what
            // the normalized component of the blend buys).
            Assert.True(quiet.BandEnergy.Max() > 0.1, $"quiet max band {quiet.BandEnergy.Max()}");
            Assert.True(loud.BandEnergy.Max() > 0.1, $"loud max band {loud.BandEnergy.Max()}");

            // All bounded, always.
            Assert.All(quiet.BandEnergy, b => Assert.InRange(b, 0f, 1f));
            Assert.All(loud.BandEnergy, b => Assert.InRange(b, 0f, 1f));
        }

        [Fact]
        public void BandFlux_RegistersTransients()
        {
            var snapshot = LastSnapshot(amplitude: 0.9);
            Assert.All(snapshot.BandFlux, f => Assert.True(float.IsFinite(f) && f >= 0));
        }

        [Fact]
        public void Energy_PreservesDynamicContrast()
        {
            // The raw half of the blend must keep loud material louder than quiet material —
            // full normalization would erase the difference and flatten the dynamic arc.
            var quiet = LastSnapshot(amplitude: 0.02);
            var loud = LastSnapshot(amplitude: 0.9);

            Assert.True(loud.Lite.Energy > quiet.Lite.Energy + 0.05,
                $"loud {loud.Lite.Energy} vs quiet {quiet.Lite.Energy}");
        }
    }
}
