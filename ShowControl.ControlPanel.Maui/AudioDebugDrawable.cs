using System;
using ShowControl.AudioAnalysis;

namespace ShowControl.ControlPanel.Maui
{
    /// <summary>
    /// The audio debug view: AudioLite meters, band bars and phase wheels, redrawn from the
    /// latest published snapshot at the page's timer rate. Per-frame geometry against the
    /// control's own size is deliberately platform code (the FFT-polyline precedent), and —
    /// as with every drawable on this branch — an exception leaving <see cref="Draw"/> on
    /// Mac Catalyst kills the show, not the panel, so the whole body is caught and logged
    /// (once, not per tick).
    /// </summary>
    public sealed class AudioDebugDrawable : IDrawable
    {
        private static readonly string[] LiteLabels = { "energy", "impulse", "tension", "phase", "hue", "grain", "motion", "spread" };
        private static readonly string[] WheelLabels = { "beat", "bar", "display" };

        private bool _loggedDrawFailure;

        /// <summary>Supplies the frame to draw; set once by the page after the engine exists.</summary>
        public Func<AnalysisSnapshot>? SnapshotProvider { get; set; }

        public void Draw(ICanvas canvas, RectF rect)
        {
            try
            {
                var snapshot = SnapshotProvider?.Invoke() ?? AnalysisSnapshot.Empty;
                DrawCore(canvas, rect, snapshot);
            }
            catch (Exception ex)
            {
                if (!_loggedDrawFailure)
                {
                    _loggedDrawFailure = true;
                    Serilog.Log.Error(ex, "[AudioDebugDrawable] Draw failed; further failures suppressed.");
                }
            }
        }

        private static void DrawCore(ICanvas canvas, RectF rect, AnalysisSnapshot snapshot)
        {
            canvas.FillColor = Colors.Black;
            canvas.FillRectangle(rect);

            float pad = 8;
            float columnWidth = (rect.Width - pad * 4) / 3;
            var metersRect = new RectF(rect.X + pad, rect.Y + pad, columnWidth, rect.Height - pad * 2);
            var bandsRect = new RectF(metersRect.Right + pad, rect.Y + pad, columnWidth, rect.Height - pad * 2);
            var wheelsRect = new RectF(bandsRect.Right + pad, rect.Y + pad, columnWidth, rect.Height - pad * 2);

            DrawLiteMeters(canvas, metersRect, snapshot.Lite);
            DrawBands(canvas, bandsRect, snapshot);
            DrawPhaseWheels(canvas, wheelsRect, snapshot);
        }

        private static void DrawLiteMeters(ICanvas canvas, RectF rect, AudioLiteFrame lite)
        {
            // Motion's documented range is 0..~3; everything else is 0..1.
            Span<float> values = stackalloc float[]
            {
                lite.Energy, lite.Impulse, lite.Tension, lite.Phase,
                lite.Hue, lite.Grain, lite.Motion / 3f, lite.Spread,
            };

            float rowHeight = rect.Height / values.Length;
            float labelWidth = 60;
            canvas.FontSize = 11;
            for (int i = 0; i < values.Length; i++)
            {
                float y = rect.Y + i * rowHeight;
                var barArea = new RectF(rect.X + labelWidth, y + 3, rect.Width - labelWidth, rowHeight - 6);

                canvas.FontColor = Colors.Silver;
                canvas.DrawString(LiteLabels[i], rect.X, y, labelWidth - 4, rowHeight,
                    HorizontalAlignment.Left, VerticalAlignment.Center);

                canvas.StrokeColor = Colors.DimGray;
                canvas.StrokeSize = 1;
                canvas.DrawRectangle(barArea);
                canvas.FillColor = i == 1 ? Colors.OrangeRed : Colors.MediumSeaGreen;
                float w = Math.Clamp(values[i], 0f, 1f) * barArea.Width;
                canvas.FillRectangle(barArea.X, barArea.Y, w, barArea.Height);
            }
        }

        private static void DrawBands(ICanvas canvas, RectF rect, AnalysisSnapshot snapshot)
        {
            var bands = snapshot.BandEnergy;
            float barWidth = rect.Width / bands.Length;
            for (int i = 0; i < bands.Length; i++)
            {
                float h = Math.Clamp(bands[i], 0f, 1f) * rect.Height;
                canvas.FillColor = Colors.SteelBlue;
                canvas.FillRectangle(rect.X + i * barWidth + 1, rect.Bottom - h, barWidth - 2, h);
            }
            canvas.StrokeColor = Colors.DimGray;
            canvas.StrokeSize = 1;
            canvas.DrawRectangle(rect);

            // Level strip under the frame border: rms (filled) and peak (tick), both 0..1.
            float rmsW = Math.Clamp(snapshot.Rms, 0f, 1f) * rect.Width;
            float peakX = rect.X + Math.Clamp(snapshot.Peak, 0f, 1f) * rect.Width;
            canvas.FillColor = Colors.MediumSeaGreen;
            canvas.FillRectangle(rect.X, rect.Bottom - 4, rmsW, 4);
            canvas.StrokeColor = Colors.White;
            canvas.DrawLine(peakX, rect.Bottom - 6, peakX, rect.Bottom);
        }

        private static void DrawPhaseWheels(ICanvas canvas, RectF rect, AnalysisSnapshot snapshot)
        {
            Span<float> phases = stackalloc float[] { snapshot.BeatPhase, snapshot.BarPhase, snapshot.Lite.Phase };

            float cellHeight = rect.Height / phases.Length;
            canvas.FontSize = 11;
            for (int i = 0; i < phases.Length; i++)
            {
                float cy = rect.Y + i * cellHeight + cellHeight / 2;
                float radius = Math.Min(cellHeight / 2 - 12, rect.Width / 2 - 40);
                if (radius < 6)
                {
                    continue;
                }
                float cx = rect.X + radius + 4;

                canvas.StrokeColor = Colors.DimGray;
                canvas.StrokeSize = 1;
                canvas.DrawCircle(cx, cy, radius);
                // Phase 0 at 12 o'clock, advancing clockwise.
                double angle = phases[i] * 2 * Math.PI - Math.PI / 2;
                canvas.StrokeColor = i == 2 ? Colors.Gold : Colors.MediumSeaGreen;
                canvas.StrokeSize = 2;
                canvas.DrawLine(cx, cy,
                    cx + (float)(radius * Math.Cos(angle)),
                    cy + (float)(radius * Math.Sin(angle)));

                canvas.FontColor = Colors.Silver;
                canvas.DrawString(WheelLabels[i], cx + radius + 6, cy - 8, rect.Right - (cx + radius + 6), 16,
                    HorizontalAlignment.Left, VerticalAlignment.Center);
            }
        }
    }
}
