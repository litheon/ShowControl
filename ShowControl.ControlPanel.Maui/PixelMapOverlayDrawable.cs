using ShowControl.PixelMapping;
using System;
using System.Collections.Generic;

namespace ShowControl.ControlPanel.Maui
{
    /// <summary>
    /// Draws the pixel map over the live shader preview.
    /// <para>
    /// Positions are stored in shader-image space (1080x960). The preview blits that fixed image to
    /// fill the whole surface with an independent scale on each axis, so converting is a plain
    /// per-axis ratio against the overlay's own bounds — no DPI factor, no letterbox offset. See
    /// <see cref="PixelMapOverlayTransform"/>.
    /// </para>
    /// </summary>
    public sealed class PixelMapOverlayDrawable : IDrawable
    {
        /// <summary>Above this many pixels, dots are thinned out; the connecting line still shows the full run.</summary>
        private const int MaxDotsPerFixture = 240;

        private static readonly Color SelectedColor = Colors.Yellow;
        private static readonly Color EnabledColor = Colors.White;
        private static readonly Color DisabledColor = Color.FromRgba(255, 255, 255, 90);

        public PixelMap Map { get; set; }

        public Guid? SelectedFixtureId { get; set; }

        /// <summary>Dimmed while the preview beneath is not being presented, during a window resize.</summary>
        public bool Dimmed { get; set; }

        public void Draw(ICanvas canvas, RectF dirtyRect)
        {
            var map = Map;
            if (map == null || dirtyRect.Width <= 0 || dirtyRect.Height <= 0)
            {
                return;
            }

            var transform = new PixelMapOverlayTransform(map, dirtyRect.Width, dirtyRect.Height);
            canvas.Alpha = Dimmed ? 0.35f : 1f;

            foreach (var fixture in map.Fixtures)
            {
                DrawFixture(canvas, transform, fixture);
            }

            canvas.Alpha = 1f;
        }

        private void DrawFixture(ICanvas canvas, PixelMapOverlayTransform transform, FixtureDefinition fixture)
        {
            int pixelCount = fixture.PixelCount;
            if (pixelCount == 0)
            {
                return;
            }

            bool selected = SelectedFixtureId.HasValue && SelectedFixtureId.Value == fixture.Id;
            var color = !fixture.Enabled ? DisabledColor : selected ? SelectedColor : EnabledColor;

            canvas.StrokeColor = color;
            canvas.StrokeSize = selected ? 2.5f : 1.5f;

            var path = new PathF();
            for (int i = 0; i < pixelCount; i++)
            {
                var point = transform.ToOverlay(fixture, i);
                if (i == 0)
                {
                    path.MoveTo(point.X, point.Y);
                }
                else
                {
                    path.LineTo(point.X, point.Y);
                }
            }
            canvas.DrawPath(path);

            // Blanked pixels are drawn hollow so a dead tail is visible without reading the file.
            int step = Math.Max(1, pixelCount / MaxDotsPerFixture);
            float radius = selected ? 2.5f : 1.75f;
            for (int i = 0; i < pixelCount; i += step)
            {
                var point = transform.ToOverlay(fixture, i);
                if (fixture.IsBlanked(i))
                {
                    canvas.StrokeSize = 1f;
                    canvas.DrawCircle(point.X, point.Y, radius);
                }
                else
                {
                    canvas.FillColor = color;
                    canvas.FillCircle(point.X, point.Y, radius);
                }
            }

            // Mark the first pixel so a fixture's direction is readable at a glance.
            var head = transform.ToOverlay(fixture, 0);
            canvas.StrokeColor = color;
            canvas.StrokeSize = 1.5f;
            canvas.DrawCircle(head.X, head.Y, selected ? 6f : 4.5f);

            if (selected && !string.IsNullOrWhiteSpace(fixture.Name))
            {
                canvas.FontColor = color;
                canvas.FontSize = 12;
                canvas.DrawString(fixture.Name, head.X + 8f, head.Y - 16f, HorizontalAlignment.Left);
            }
        }
    }

    /// <summary>
    /// Converts between overlay points and shader-image coordinates.
    /// <para>
    /// Recomputed on every size change and never cached across one: the swapchain extent is
    /// debounced 100ms behind the view and reports device pixels on Mac but DIPs on Windows, so the
    /// overlay's own bounds are the only trustworthy reference. Both compositing stages are full
    /// stretches, so the display scale factor cancels and must not be applied.
    /// </para>
    /// </summary>
    public readonly struct PixelMapOverlayTransform
    {
        private readonly double _overlayWidth;
        private readonly double _overlayHeight;

        public PixelMapOverlayTransform(PixelMap map, double overlayWidth, double overlayHeight)
        {
            ImageWidth = map.ImageWidth;
            ImageHeight = map.ImageHeight;
            _overlayWidth = overlayWidth;
            _overlayHeight = overlayHeight;
        }

        public int ImageWidth { get; }

        public int ImageHeight { get; }

        public bool IsUsable => _overlayWidth > 0 && _overlayHeight > 0;

        public PointF ToOverlay(FixtureDefinition fixture, int pixelIndex)
        {
            var position = PixelMapGeometry.Resolve(fixture, pixelIndex);
            return ToOverlay(position.X, position.Y);
        }

        public PointF ToOverlay(double imageX, double imageY) => new PointF(
            (float)(imageX * _overlayWidth / ImageWidth),
            (float)(imageY * _overlayHeight / ImageHeight));

        /// <summary>Overlay point to shader-image coordinates. Not clamped: off-image pixels are meaningful.</summary>
        public (double X, double Y) ToImage(double overlayX, double overlayY) => (
            overlayX * ImageWidth / _overlayWidth,
            overlayY * ImageHeight / _overlayHeight);

        /// <summary>Image-space distance covered by one overlay point, for hit-test thresholds.</summary>
        public double ImageUnitsPerOverlayPoint => Math.Max(
            ImageWidth / Math.Max(_overlayWidth, 1d),
            ImageHeight / Math.Max(_overlayHeight, 1d));
    }
}
