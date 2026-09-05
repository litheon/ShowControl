using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using ShowControl.AudioAnalysis;

namespace ShowControl.ControlPanel.Editor
{
    /// <summary>
    /// The audio debug view's state: numerals for every published analysis field, and the
    /// synthetic-preset commands that exercise them before (or without) live capture. The
    /// page's drawable reads <see cref="Snapshot"/> directly for the bars and phase wheels;
    /// the formatted strings are the exact-value numerals beside it. Deliberately ugly,
    /// deliberately complete — a missing field gets found here, where changing it is free.
    /// <para>
    /// Lives in the editor project (plain net9.0) so it is testable; the page owns the
    /// timer that calls <see cref="Refresh"/> and the drawable that renders the snapshot.
    /// </para>
    /// </summary>
    public sealed partial class AudioDebugViewModel : EditorViewModelBase
    {
        private readonly AudioAnalysisEngine _engine;
        private readonly SyntheticAudioSource _synthetic;
        private readonly SyntheticBeatMetadataSource? _beatMetadata;

        [ObservableProperty] private string _liteText = "AudioLite";
        [ObservableProperty] private string _timingText = "Timing";
        [ObservableProperty] private string _featureText = "Features";
        [ObservableProperty] private string _sourceText = "Source: live";

        public AudioDebugViewModel(
            AudioAnalysisEngine engine,
            ILogger<AudioDebugViewModel>? logger,
            SyntheticBeatMetadataSource? beatMetadata = null)
            : base(logger)
        {
            _engine = engine;
            _synthetic = new SyntheticAudioSource(engine);
            _beatMetadata = beatMetadata;
        }

        /// <summary>The frame the drawable renders. Immutable; swapped by <see cref="Refresh"/>.</summary>
        public AnalysisSnapshot Snapshot { get; private set; } = AnalysisSnapshot.Empty;

        /// <summary>Pulls the engine's latest frame into the bindable surface. Called from the
        /// page's dispatcher timer (~15 Hz); cheap no-op when nothing new was published.</summary>
        public void Refresh()
        {
            var snapshot = _engine.Latest;
            if (ReferenceEquals(snapshot, Snapshot))
            {
                return;
            }
            Snapshot = snapshot;

            var lite = snapshot.Lite;
            LiteText =
                $"energy  {lite.Energy:F3}\n" +
                $"impulse {lite.Impulse:F3}\n" +
                $"tension {lite.Tension:F3}\n" +
                $"phase   {lite.Phase:F3}\n" +
                $"hue     {lite.Hue:F3}\n" +
                $"grain   {lite.Grain:F3}\n" +
                $"motion  {lite.Motion:F3}\n" +
                $"spread  {lite.Spread:F3}";
            string presence = _beatMetadata?.Presence.ToString() ?? "no source";
            TimingText =
                $"bpm {snapshot.Bpm:F1} (conf {snapshot.TempoConfidence:F2})\n" +
                $"beat  {snapshot.BeatPhase:F3}\n" +
                $"bar   {snapshot.BarPhase:F3}\n" +
                $"sect  {snapshot.Section} ({snapshot.SectionConfidence:F2})\n" +
                $"meta  {presence}\n" +
                $"frame {snapshot.FrameIndex}";
            FeatureText =
                $"rms  {snapshot.Rms:F4}  peak {snapshot.Peak:F4}\n" +
                $"flux {snapshot.FluxTotal:E2}\n" +
                $"centroid {snapshot.CentroidHz:F0} Hz\n" +
                $"flatness {snapshot.Flatness:F3}\n" +
                (snapshot.SignalPresent ? "signal PRESENT" : "signal absent");
        }

        /// <summary>Starts a synthetic preset at wall-clock pace. Parameter is the
        /// <see cref="SyntheticPreset"/> name, from the button's CommandParameter.</summary>
        [RelayCommand]
        private void StartPreset(string? presetName) => Guard("Start synthetic preset", () =>
        {
            if (!Enum.TryParse(presetName, ignoreCase: true, out SyntheticPreset preset))
            {
                SourceText = $"Unknown preset '{presetName}'";
                return;
            }
            _engine.Start();
            _synthetic.StartRealtime(preset);
            SourceText = $"Source: synthetic {preset}";
        });

        [RelayCommand]
        private void StopSynthetic() => Guard("Stop synthetic preset", () =>
        {
            _synthetic.Stop();
            SourceText = "Source: live";
        });

        /// <summary>Arms a synthetic 128 BPM full-track grid anchored at the engine's
        /// current time — exercises the PLL's grid-fusion path without CDJs on the network.</summary>
        [RelayCommand]
        private void ToggleSyntheticGrid() => Guard("Toggle synthetic beat grid", () =>
        {
            if (_beatMetadata is null)
            {
                SourceText = "No beat metadata source wired";
                return;
            }
            if (_beatMetadata.Presence == BeatMetadataPresence.FullGrid)
            {
                _beatMetadata.Clear();
                return;
            }
            const double bpm = 128;
            double firstBeat = _engine.Latest.Time;
            int tenMinutesOfBeats = (int)(bpm / 60 * 600);
            _beatMetadata.SetGrid(BeatGrid.Regular(firstBeat, bpm, tenMinutesOfBeats));
        });
    }
}
