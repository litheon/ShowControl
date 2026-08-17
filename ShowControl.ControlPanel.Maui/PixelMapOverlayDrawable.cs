using ShowControl.ControlPanel.Editor;
using ShowControl.PixelMapping;
using System;
using System.Collections.Generic;

namespace ShowControl.ControlPanel.Maui
{
    /// <summary>
    /// Draws the pixel map over the live shader preview.
    /// <para>
    /// Positions are stored in shader-image space (<c>PixelMap.ImageWidth</c> x
    /// <c>ImageHeight</c>, currently 1350x1200). The preview blits that fixed image to
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

        private bool _loggedDrawFailure;

        public PixelMap Map { get; set; }

        public Guid? SelectedFixtureId { get; set; }

        /// <summary>Dimmed while the preview beneath is not being presented, during a window resize.</summary>
        public bool Dimmed { get; set; }

        /// <summary>
        /// Part of the image the preview is showing. Set by the page's zoom; must be the same
        /// rectangle the graphics host was given, or the map drifts off the picture.
        /// </summary>
        public PreviewRegion Region { get; set; }

        /// <summary>
        /// Draws the map. Swallows failures after logging one: this is called from the platform's
        /// draw pass, where an exception is marshalled into an Objective-C one and aborts the
        /// process — losing the overlay is survivable, losing the show is not.
        /// </summary>
        public void Draw(ICanvas canvas, RectF dirtyRect)
        {
            var map = Map;
            if (map == null || dirtyRect.Width <= 0 || dirtyRect.Height <= 0)
            {
                return;
            }

            try
            {
                var region = Region.IsUsable ? Region : PreviewRegion.WholeImage(map.ImageWidth, map.ImageHeight);
                var transform = new PixelMapOverlayTransform(map, dirtyRect.Width, dirtyRect.Height, region);
                canvas.Alpha = Dimmed ? 0.35f : 1f;

                foreach (var fixture in map.Fixtures)
                {
                    DrawFixture(canvas, transform, fixture);
                }

                canvas.Alpha = 1f;
            }
            catch (Exception ex)
            {
                if (!_loggedDrawFailure)
                {
                    _loggedDrawFailure = true;
                    Serilog.Log.Error(ex, "[PixelMapOverlay] Could not draw the pixel map; further failures suppressed.");
                }
            }
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
    /// <para>
    /// Zoom is expressed as the region of the image the preview is presenting, which is exactly the
    /// source rectangle the host blits from. Both sides read the same rectangle, so the overlay
    /// stays registered with the picture underneath at any magnification — which is the whole point
    /// of zooming to align fixtures.
    /// </para>
    /// </summary>
    public readonly struct PixelMapOverlayTransform
    {
        private readonly double _overlayWidth;
        private readonly double _overlayHeight;
        private readonly double _regionX;
        private readonly double _regionY;
        private readonly double _regionWidth;
        private readonly double _regionHeight;

        public PixelMapOverlayTransform(PixelMap map, double overlayWidth, double overlayHeight)
            : this(map, overlayWidth, overlayHeight, PreviewRegion.WholeImage(map.ImageWidth, map.ImageHeight))
        {
        }

        public PixelMapOverlayTransform(PixelMap map, double overlayWidth, double overlayHeight, PreviewRegion region)
        {
            ImageWidth = map.ImageWidth;
            ImageHeight = map.ImageHeight;
            _overlayWidth = overlayWidth;
            _overlayHeight = overlayHeight;
            _regionX = region.X;
            _regionY = region.Y;
            _regionWidth = region.Width > 0 ? region.Width : map.ImageWidth;
            _regionHeight = region.Height > 0 ? region.Height : map.ImageHeight;
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
            (float)((imageX - _regionX) * _overlayWidth / _regionWidth),
            (float)((imageY - _regionY) * _overlayHeight / _regionHeight));

        /// <summary>Overlay point to shader-image coordinates. Not clamped: off-image pixels are meaningful.</summary>
        public (double X, double Y) ToImage(double overlayX, double overlayY) => (
            _regionX + (overlayX * _regionWidth / _overlayWidth),
            _regionY + (overlayY * _regionHeight / _overlayHeight));

        /// <summary>
        /// Image-space distance covered by one overlay point, for hit-test thresholds. Shrinks as
        /// the preview zooms in, so the grab radius stays the same distance on screen — and gets
        /// correspondingly finer in image units, which is what makes close alignment possible.
        /// </summary>
        public double ImageUnitsPerOverlayPoint => Math.Max(
            _regionWidth / Math.Max(_overlayWidth, 1d),
            _regionHeight / Math.Max(_overlayHeight, 1d));
    }

}
