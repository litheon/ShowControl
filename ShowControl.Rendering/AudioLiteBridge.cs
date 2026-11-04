using System;
using ShowControl.AudioAnalysis;
using ShowControl.Graphics;

namespace ShowControl.Rendering
{
    /// <summary>
    /// Forwards each published analysis snapshot's AudioLite frame — plus the current
    /// hop's FFT spectrum (with each band's injection site and color) and the drop signal — into the graphics host. This is the one narrow joint between the
    /// analysis project (which Graphics cannot reference) and the render path: one struct
    /// copy plus one 4 KB spectrum copy per publish (~10.7ms), no allocation, no locks —
    /// both SetAudio and SetAudioSpectrum are copy-then-volatile-swap on the host side.
    /// The spectrum span is only valid inside this handler (same thread, current hop),
    /// which is exactly where it is consumed.
    ///
    /// Wired once at RenderService construction and never touched by animation or shader
    /// lifecycles: audio state flows to the GPU whatever is on screen, and a shader that
    /// declares only the legacy push prefix simply never reads it.
    /// </summary>
    public sealed class AudioLiteBridge : IDisposable
    {
        private readonly AudioAnalysisEngine _engine;
        private readonly IGraphicsHost _graphicsHost;

        public AudioLiteBridge(AudioAnalysisEngine engine, IGraphicsHost graphicsHost)
        {
            _engine = engine;
            _graphicsHost = graphicsHost;
            _engine.SnapshotPublished += OnSnapshotPublished;
            _engine.Start();
        }

        /// <summary>A pad press: the fluid floods with dye from the pad's disc. Any thread.</summary>
        public void PadPressed(int disc) => _engine.PadPressed(disc);

        /// <summary>Which discs have a pad down, one bit per disc; held, a disc injects at full. Any thread.</summary>
        public void SetHeldDiscs(int discMask) => _engine.SetHeldDiscs(discMask);

        private float _lastDropCountdown = float.PositiveInfinity;
        private double _lastDropFiredAt = double.NegativeInfinity;

        private void OnSnapshotPublished(AnalysisSnapshot snapshot)
        {
            var lite = snapshot.Lite;
            _graphicsHost.SetAudio(new AudioLitePush(
                lite.Energy, lite.Impulse, lite.Tension, lite.Phase,
                lite.Hue, lite.Grain, lite.Motion, lite.Spread));
            _graphicsHost.SetAudioSpectrum(_engine.CurrentSpectrum, _engine.CurrentInjectionTable);
            _graphicsHost.SetDropSignal(ComputeDropSignal(snapshot));
        }

        /// <summary>
        /// Folds the snapshot's announced and waveform-predicted drop countdowns into the
        /// one signed float shaders see: positive counts down to the nearest drop,
        /// negative counts up from one that just fired (detected when a short countdown
        /// vanishes or jumps forward — the marker slid into the past between hops), and
        /// 999 means none known. Analysis-thread only, like the rest of this handler.
        /// </summary>
        private float ComputeDropSignal(AnalysisSnapshot snapshot)
        {
            var next = float.PositiveInfinity;
            if (snapshot.NextDropIn >= 0f) next = snapshot.NextDropIn;
            if (snapshot.NextPredictedDropIn >= 0f) next = Math.Min(next, snapshot.NextPredictedDropIn);

            if (_lastDropCountdown < 1.2f
                && (float.IsPositiveInfinity(next) || next > _lastDropCountdown + 1.5f))
            {
                _lastDropFiredAt = snapshot.Time;
            }
            _lastDropCountdown = next;

            var sinceDrop = snapshot.Time - _lastDropFiredAt;
            if (sinceDrop >= 0 && sinceDrop < 2.0) return (float)-sinceDrop;
            return float.IsPositiveInfinity(next) ? 999f : Math.Min(next, 999f);
        }

        public void Dispose() => _engine.SnapshotPublished -= OnSnapshotPublished;
    }
}
