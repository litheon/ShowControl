using System;
using ShowControl.AudioAnalysis;
using Xunit;

namespace ShowControl.Tests
{
    public class AutoGainTests
    {
        private const int Rate = 48_000;
        private const int Block = 512;

        /// <summary>Runs a sine of the given amplitude for some seconds, returning the
        /// peak of the scaled output over the last second.</summary>
        private static float Run(AutoGain gain, double amplitude, double seconds, long startSample = 0)
        {
            var block = new float[Block];
            float lastSecondPeak = 0f;
            int blocks = (int)(seconds * Rate / Block);
            for (int b = 0; b < blocks; b++)
            {
                for (int i = 0; i < Block; i++)
                {
                    block[i] = (float)(amplitude * Math.Sin(2 * Math.PI * 100.0 * (startSample + b * Block + i) / Rate));
                }
                gain.Process(block, (double)Block / Rate);
                if (b >= blocks - Rate / Block)
                {
                    foreach (float s in block) lastSecondPeak = Math.Max(lastSecondPeak, Math.Abs(s));
                }
            }
            return lastSecondPeak;
        }

        [Fact]
        public void TheLoudestLevelInTheWindow_Becomes100Percent()
        {
            // The gain only ever RISES gradually (a few seconds), so give it time to
            // settle on the reference.
            var gain = new AutoGain(new AnalysisSettings());
            float peak = Run(gain, amplitude: 0.25, seconds: 20);
            Assert.InRange(peak, 0.97f, 1.0f);
            Assert.Equal(4f, gain.Gain, precision: 1);
        }

        [Fact]
        public void ALouderMoment_PullsTheGainDownAtOnce_SoNothingExceedsFull()
        {
            var gain = new AutoGain(new AnalysisSettings());
            Run(gain, amplitude: 0.25, seconds: 5);
            float peak = Run(gain, amplitude: 0.8, seconds: 0.1);
            Assert.InRange(peak, 0.9f, 1.0f);
        }

        [Fact]
        public void AQuietPassage_StaysQuiet_WhileTheLoudReferenceIsInTheWindow()
        {
            var gain = new AutoGain(new AnalysisSettings());
            Run(gain, amplitude: 0.8, seconds: 5);
            float peak = Run(gain, amplitude: 0.2, seconds: 60);
            // 0.2 against a 0.8 reference: a quarter, with the gain unchanged.
            Assert.InRange(peak, 0.23f, 0.27f);
        }

        [Fact]
        public void OnceTheReferenceExpires_TheQuietPassageSwellsToFull()
        {
            var settings = new AnalysisSettings { AutoGainWindowSeconds = 20 };
            var gain = new AutoGain(settings);
            Run(gain, amplitude: 0.8, seconds: 5);
            float peak = Run(gain, amplitude: 0.2, seconds: 20 + 15);
            Assert.InRange(peak, 0.95f, 1.0f);
        }

        [Fact]
        public void SilenceIsNotAmplifiedIntoSignal()
        {
            var settings = new AnalysisSettings();
            var gain = new AutoGain(settings);
            float peak = Run(gain, amplitude: 0.0005, seconds: 10);
            // Capped by the floor: at most 1/floor times the input.
            Assert.True(peak <= 0.0005f / (float)settings.AutoGainFloor * 1.01f, $"peak {peak}");
            Assert.True(peak < 0.05f);
        }

        [Fact]
        public void CanBeSwitchedOff()
        {
            var gain = new AutoGain(new AnalysisSettings { AutoGainEnabled = false });
            float peak = Run(gain, amplitude: 0.25, seconds: 3);
            Assert.Equal(0.25f, peak, precision: 2);
            Assert.Equal(1f, gain.Gain);
        }
    }
}
