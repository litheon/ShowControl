using System;
using ShowControl.AudioAnalysis;
using ShowControl.Graphics;

namespace ShowControl.Rendering
{
    /// <summary>
    /// Forwards each published analysis snapshot's AudioLite frame into the graphics host's
    /// push-constant state. This is the one narrow joint between the analysis project (which
    /// Graphics cannot reference) and the render path: one struct copy per publish (~10.7ms),
    /// no allocation, no locks — SetAudio is a volatile reference swap.
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

        private void OnSnapshotPublished(AnalysisSnapshot snapshot)
        {
            var lite = snapshot.Lite;
            _graphicsHost.SetAudio(new AudioLitePush(
                lite.Energy, lite.Impulse, lite.Tension, lite.Phase,
                lite.Hue, lite.Grain, lite.Motion, lite.Spread));
        }

        public void Dispose() => _engine.SnapshotPublished -= OnSnapshotPublished;
    }
}
