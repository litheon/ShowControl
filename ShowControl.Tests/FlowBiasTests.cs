using System;
using ShowControl.AudioAnalysis;
using Xunit;

namespace ShowControl.Tests
{
    public class FlowBiasTests
    {
        private const double Hop = 512.0 / 48_000;

        private static void Run(FlowBias bias, double seconds, double energy, double tension)
        {
            int hops = (int)Math.Round(seconds / Hop);
            for (int i = 0; i < hops; i++)
            {
                bias.Advance(Hop, energy, tension, -1, false);
            }
        }

        [Fact]
        public void EachDrop_SwitchesToADifferentForm_AtOnce()
        {
            var bias = new FlowBias();
            Run(bias, 5, energy: 1, tension: 0);
            Assert.Equal(FlowBiasMode.Right, bias.Mode);

            var previous = bias.Mode;
            for (int drop = 0; drop < 6; drop++)
            {
                bias.Advance(Hop, 1, 0, -1, dropFired: true);
                Assert.NotEqual(previous, bias.Mode);
                previous = bias.Mode;
                Run(bias, 2, energy: 1, tension: 0);
                Assert.Equal(previous, bias.Mode);
            }
        }

        [Fact]
        public void TheBiasIsOneFormAtATime_NeverAMix()
        {
            var bias = new FlowBias();
            for (int i = 0; i < 30_000; i++)
            {
                bias.Advance(Hop, (i % 500) / 500.0, (i % 1300) / 1300.0, (i % 2000) - 1000, i % 700 == 0);
                switch (bias.Mode)
                {
                    case FlowBiasMode.Right:
                        Assert.True(bias.Field > 0);
                        Assert.Equal(0f, bias.Split);
                        break;
                    case FlowBiasMode.Left:
                        Assert.True(bias.Field < 0);
                        Assert.Equal(0f, bias.Split);
                        break;
                    default:
                        Assert.Equal(0f, bias.Field);
                        Assert.True(bias.Split > 0);
                        break;
                }
            }
        }

        [Fact]
        public void TheBiasCarriesTheInjectionPoints_ExceptWhileSplit()
        {
            var bias = new FlowBias();
            Run(bias, 20, energy: 1, tension: 0);

            // Right: the points have been carried right, at the field's own speed.
            float carried = bias.Offset;
            Assert.True(carried > 0.01f && carried < 0.5f, $"offset {carried}");
            float before = bias.Offset;
            Run(bias, 10, energy: 1, tension: 0);
            Assert.InRange(bias.Offset - before, bias.Field * 10 * 0.99f, bias.Field * 10 * 1.01f);

            // Left: carried back the other way.
            bias.Advance(Hop, 1, 0, -1, dropFired: true);
            before = bias.Offset;
            Run(bias, 5, energy: 1, tension: 0);
            Assert.True(bias.Offset < before);

            // Split: they stay where they are.
            bias.Advance(Hop, 1, 0, -1, dropFired: true);
            before = bias.Offset;
            Run(bias, 10, energy: 1, tension: 0);
            Assert.Equal(before, bias.Offset, precision: 6);
        }

        [Fact]
        public void SpeedSinksInASlowSection_AndClimbsThroughABuild()
        {
            var groove = new FlowBias();
            Run(groove, 15, energy: 0.8, tension: 0);

            var slow = new FlowBias();
            Run(slow, 15, energy: 0.2, tension: 0);

            var build = new FlowBias();
            Run(build, 15, energy: 0.8, tension: 1);

            Assert.True(slow.Speed < groove.Speed * 0.6f, $"slow {slow.Speed} groove {groove.Speed}");
            Assert.True(build.Speed > groove.Speed * 2f, $"build {build.Speed} groove {groove.Speed}");
        }

        [Fact]
        public void AKnownDrop_RampsTheSpeedUp_GentlyThenSteeply()
        {
            var bias = new FlowBias();
            Run(bias, 10, energy: 0.8, tension: 0);
            float groove = bias.Speed;

            // Count down to a drop with no inferred tension at all: speed climbs the
            // whole way, and gains more in the last quarter than in the first.
            var speeds = new System.Collections.Generic.List<float>();
            double remaining = FlowBias.RampSeconds;
            while (remaining > 0)
            {
                bias.Advance(Hop, 0.8, 0, remaining, false);
                speeds.Add(bias.Speed);
                remaining -= Hop;
            }
            for (int i = 1; i < speeds.Count; i++)
            {
                Assert.True(speeds[i] >= speeds[i - 1] - 1e-6f, $"fell at {i}");
            }
            int quarter = speeds.Count / 4;
            float early = speeds[quarter] - speeds[0];
            float late = speeds[^1] - speeds[^(quarter + 1)];
            Assert.True(late > early * 2, $"early {early} late {late}");
            Assert.True(speeds[^1] > groove * 2, $"end {speeds[^1]} groove {groove}");
        }

        [Fact]
        public void WithoutMetadata_InferredTensionAloneRamps()
        {
            Assert.Equal(0, FlowBias.BuildOf(0, -1));
            Assert.Equal(0.5, FlowBias.BuildOf(0.5, -1));
            Assert.Equal(1, FlowBias.BuildOf(1, -1));
            // The countdown takes over only when it is further along than the tension.
            Assert.Equal(0.5, FlowBias.BuildOf(0.5, FlowBias.RampSeconds));
            Assert.Equal(0.75, FlowBias.BuildOf(0.5, FlowBias.RampSeconds / 4), precision: 6);
        }

        [Fact]
        public void SpeedSwells_RatherThanSteps()
        {
            var bias = new FlowBias();
            Run(bias, 10, energy: 0.2, tension: 0);
            float before = bias.Speed;
            bias.Advance(Hop, 1, 1, -1, false);
            Assert.True(Math.Abs(bias.Speed - before) < 0.05f);
        }

        [Fact]
        public void SurvivesGarbage()
        {
            var bias = new FlowBias();
            bias.Advance(double.NaN, double.NaN, double.NaN, double.NaN, true);
            bias.Advance(Hop, double.NaN, double.PositiveInfinity, double.NegativeInfinity, false);
            Assert.InRange(bias.Speed, 0f, (float)FlowBias.BuildGain);
        }
    }
}
