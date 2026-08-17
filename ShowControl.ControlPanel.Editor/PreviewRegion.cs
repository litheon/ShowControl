using System;

namespace ShowControl.ControlPanel.Editor
{
    /// <summary>
    /// The rectangle of the shader image the preview is showing, in image coordinates. The whole
    /// image at zoom 1; a smaller rectangle as the editor zooms in.
    /// <para>
    /// This is the one description of "where the preview is looking", and both halves of the zoom
    /// read it: the video is magnified by scaling the surface view about an anchor, and the overlay
    /// is redrawn from this rectangle. They are computed independently and must agree, so any error
    /// in here shows up immediately as the map drifting off the picture.
    /// </para>
    /// <para>
    /// The region always keeps the image's aspect ratio — it is derived from a single magnification
    /// rather than free-form — so zooming can never introduce a stretch on top of the one the
    /// preview already applies when it fills a window of a different shape.
    /// </para>
    /// </summary>
    public readonly struct PreviewRegion : IEquatable<PreviewRegion>
    {
        /// <summary>Below this the anchor is meaningless and the region is the whole image.</summary>
        private const double UnzoomedEpsilon = 1e-9;

        public PreviewRegion(double x, double y, double width, double height)
        {
            X = x;
            Y = y;
            Width = width;
            Height = height;
        }

        public double X { get; }

        public double Y { get; }

        public double Width { get; }

        public double Height { get; }

        public double CentreX => X + (Width / 2d);

        public double CentreY => Y + (Height / 2d);

        public bool IsUsable => Width > 0d && Height > 0d;

        public static PreviewRegion WholeImage(int imageWidth, int imageHeight) =>
            new PreviewRegion(0d, 0d, imageWidth, imageHeight);

        /// <summary>
        /// The region visible when the surface view is scaled by <paramref name="zoom"/> about the
        /// anchor point (<paramref name="anchorX"/>, <paramref name="anchorY"/>), each in [0,1] of
        /// the view.
        /// <para>
        /// MAUI applies the layer transform about the anchor and compensates for the anchor's own
        /// offset, so a point at the anchor maps to itself and everything else scales about it:
        /// <c>screen(u) = A + zoom·(u − A)</c>. The visible width is therefore <c>1/zoom</c> of the
        /// view and its left edge is at <c>anchorX·(zoom−1)/zoom</c> of it.
        /// </para>
        /// </summary>
        public static PreviewRegion ForZoom(int imageWidth, int imageHeight, double zoom, double anchorX, double anchorY)
        {
            if (zoom <= 1d + UnzoomedEpsilon)
            {
                return WholeImage(imageWidth, imageHeight);
            }

            anchorX = Clamp01(anchorX);
            anchorY = Clamp01(anchorY);

            double width = imageWidth / zoom;
            double height = imageHeight / zoom;
            return new PreviewRegion(
                anchorX * (imageWidth - width),
                anchorY * (imageHeight - height),
                width,
                height);
        }

        /// <summary>
        /// The anchor that puts a region's top-left corner where it is. The inverse of
        /// <see cref="ForZoom"/>, and the reason zoom is expressed as an anchor at all: the anchor's
        /// own [0,1] range maps exactly onto the range of positions where the region still lies
        /// inside the image, so clamping the anchor <em>is</em> the clamp — there is no second
        /// constraint that could fall out of step with it.
        /// </summary>
        public static (double AnchorX, double AnchorY) AnchorFor(
            int imageWidth, int imageHeight, double zoom, double regionX, double regionY)
        {
            if (zoom <= 1d + UnzoomedEpsilon)
            {
                return (0.5d, 0.5d);
            }

            double spareX = imageWidth - (imageWidth / zoom);
            double spareY = imageHeight - (imageHeight / zoom);

            return (
                spareX > 0d ? Clamp01(regionX / spareX) : 0.5d,
                spareY > 0d ? Clamp01(regionY / spareY) : 0.5d);
        }

        /// <summary>
        /// Moves the view by an image-space delta, clamped so it cannot leave the image. Returns the
        /// anchor for the result, which is what the view applies.
        /// </summary>
        public static (double AnchorX, double AnchorY) PannedBy(
            int imageWidth, int imageHeight, double zoom, double anchorX, double anchorY, double dx, double dy)
        {
            var region = ForZoom(imageWidth, imageHeight, zoom, anchorX, anchorY);
            return AnchorFor(imageWidth, imageHeight, zoom, region.X + dx, region.Y + dy);
        }

        /// <summary>
        /// Zooms by <paramref name="factor"/> while holding the image point under
        /// (<paramref name="focusU"/>, <paramref name="focusV"/>) — a fraction of the view, so a
        /// pinch origin or 0.5 for a button — still under it afterwards.
        /// </summary>
        public static (double Zoom, double AnchorX, double AnchorY) ZoomedAbout(
            int imageWidth, int imageHeight,
            double zoom, double anchorX, double anchorY,
            double factor, double focusU, double focusV,
            double minZoom, double maxZoom)
        {
            double target = Math.Clamp(zoom * factor, minZoom, maxZoom);
            if (target <= 1d + UnzoomedEpsilon)
            {
                return (Math.Max(minZoom, 1d), 0.5d, 0.5d);
            }

            var before = ForZoom(imageWidth, imageHeight, zoom, anchorX, anchorY);
            double focusImageX = before.X + (focusU * before.Width);
            double focusImageY = before.Y + (focusV * before.Height);

            // Where the region has to start for that image point to land back under the same
            // fraction of the view at the new magnification.
            double nextX = focusImageX - (focusU * (imageWidth / target));
            double nextY = focusImageY - (focusV * (imageHeight / target));

            var (nextAnchorX, nextAnchorY) = AnchorFor(imageWidth, imageHeight, target, nextX, nextY);
            return (target, nextAnchorX, nextAnchorY);
        }

        private static double Clamp01(double value) => Math.Clamp(value, 0d, 1d);

        public bool Equals(PreviewRegion other) =>
            X.Equals(other.X) && Y.Equals(other.Y) && Width.Equals(other.Width) && Height.Equals(other.Height);

        public override bool Equals(object? obj) => obj is PreviewRegion other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(X, Y, Width, Height);

        public override string ToString() =>
            $"{Width:0.##}x{Height:0.##} at ({X:0.##}, {Y:0.##})";
    }
}
