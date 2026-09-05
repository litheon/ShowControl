using System;
using ShowControl.AudioAnalysis;
using ShowControl.ControlPanel.Editor;
using Xunit;

namespace ShowControl.Tests
{
    public class AudioDebugViewModelTests
    {
        [Fact]
        public void Refresh_BeforeAnyPublish_KeepsValidDefaults()
        {
            using var engine = new AudioAnalysisEngine();
            var vm = new AudioDebugViewModel(engine, logger: null);

            vm.Refresh();

            Assert.Same(AnalysisSnapshot.Empty, vm.Snapshot);
        }

        [Fact]
        public void Refresh_AfterPublish_SurfacesEveryReadout()
        {
            using var engine = new AudioAnalysisEngine();
            var vm = new AudioDebugViewModel(engine, logger: null);
            var source = new SyntheticAudioSource(engine);

            source.Generate(SyntheticPreset.NoiseFloorWithKicks, seconds: 1.0);
            engine.ProcessPending();
            vm.Refresh();

            Assert.NotSame(AnalysisSnapshot.Empty, vm.Snapshot);
            Assert.Contains("energy", vm.LiteText);
            Assert.Contains("impulse", vm.LiteText);
            Assert.Contains("bpm", vm.TimingText);
            Assert.Contains("rms", vm.FeatureText);
            Assert.Contains("signal PRESENT", vm.FeatureText);
        }

        [Fact]
        public void Refresh_WithNothingNew_IsAStableNoOp()
        {
            using var engine = new AudioAnalysisEngine();
            var vm = new AudioDebugViewModel(engine, logger: null);
            var source = new SyntheticAudioSource(engine);

            source.Generate(SyntheticPreset.Silence, seconds: 0.2);
            engine.ProcessPending();
            vm.Refresh();
            var snapshot = vm.Snapshot;
            var text = vm.LiteText;

            vm.Refresh();

            Assert.Same(snapshot, vm.Snapshot);
            Assert.Equal(text, vm.LiteText);
        }

        [Fact]
        public void StartPreset_WithUnknownName_ReportsInsteadOfThrowing()
        {
            using var engine = new AudioAnalysisEngine();
            var vm = new AudioDebugViewModel(engine, logger: null);

            vm.StartPresetCommand.Execute("NotARealPreset");

            Assert.Contains("Unknown preset", vm.SourceText);
        }

        [Fact]
        public void StartAndStopPreset_UpdateTheSourceLine()
        {
            using var engine = new AudioAnalysisEngine();
            var vm = new AudioDebugViewModel(engine, logger: null);

            vm.StartPresetCommand.Execute("ClickTrack");
            Assert.Contains("ClickTrack", vm.SourceText);

            vm.StopSyntheticCommand.Execute(null);
            Assert.Contains("live", vm.SourceText);
        }
    }
}
