using ShowControl.ControlPanel.Editor;
using ShowControl.PixelMapping;
using Xunit;

namespace ShowControl.Tests
{
    /// <summary>
    /// The preview zoom is computed twice from the same state — once as a scale and anchor on the
    /// video surface, once as a rectangle the overlay redraws from — and the two only agree if this
    /// arithmetic is right. When it is wrong the map slides off the picture, which looks exactly
    /// like a rendering bug, so it is pinned here rather than eyeballed on the rig.
    /// </summary>
    public class PreviewRegionTests
    {
        private const int W = PixelMap.DefaultImageWidth;   // 1350
        private const int H = PixelMap.DefaultImageHeight;  // 1200

        [Fact]
        public void AtZoomOne_TheWholeImageIsVisible_WhateverTheAnchor()
        {
            foreach (double anchor in new[] { 0d, 0.5d, 1d })
            {
                var region = PreviewRegion.ForZoom(W, H, 1d, anchor, anchor);

                Assert.Equal(0d, region.X);
                Assert.Equal(0d, region.Y);
                Assert.Equal(W, region.Width);
                Assert.Equal(H, region.Height);
            }
        }

        [Theory]
        [InlineData(2d)]
        [InlineData(4d)]
        [InlineData(32d)]
        public void Zooming_ShowsOneOverZoomOfEachAxis_KeepingTheImagesAspectRatio(double zoom)
        {
            var region = PreviewRegion.ForZoom(W, H, zoom, 0.5d, 0.5d);

            Assert.Equal(W / zoom, region.Width, 6);
            Assert.Equal(H / zoom, region.Height, 6);
            Assert.Equal((double)W / H, region.Width / region.Height, 6);
        }

        [Fact]
        public void CentredAnchor_KeepsTheViewOnTheMiddleOfTheImage()
        {
            var region = PreviewRegion.ForZoom(W, H, 4d, 0.5d, 0.5d);

            Assert.Equal(W / 2d, region.CentreX, 6);
            Assert.Equal(H / 2d, region.CentreY, 6);
        }

        /// <summary>
        /// The anchor's own range is the clamp: 0 and 1 have to land exactly on the image's corners
        /// with nothing beyond them, or a fully panned view would show a gutter of nothing.
        /// </summary>
        [Theory]
        [InlineData(2d)]
        [InlineData(32d)]
        public void AnchorZeroAndOne_LandExactlyOnTheImageCorners(double zoom)
        {
            var topLeft = PreviewRegion.ForZoom(W, H, zoom, 0d, 0d);
            Assert.Equal(0d, topLeft.X);
            Assert.Equal(0d, topLeft.Y);

            var bottomRight = PreviewRegion.ForZoom(W, H, zoom, 1d, 1d);
            Assert.Equal(W, bottomRight.X + bottomRight.Width, 6);
            Assert.Equal(H, bottomRight.Y + bottomRight.Height, 6);
        }

        [Theory]
        [InlineData(-3d, 0d)]
        [InlineData(4d, 1d)]
        public void AnchorsOutsideTheUnitRange_AreClampedRatherThanLeavingTheImage(double anchor, double expected)
        {
            var clamped = PreviewRegion.ForZoom(W, H, 8d, anchor, anchor);
            var expectedRegion = PreviewRegion.ForZoom(W, H, 8d, expected, expected);

            Assert.Equal(expectedRegion, clamped);
        }

        [Theory]
        [InlineData(2d, 0d, 0d)]
        [InlineData(4d, 0.25d, 0.75d)]
        [InlineData(32d, 1d, 1d)]
        public void AnchorAndRegion_RoundTrip(double zoom, double anchorX, double anchorY)
        {
            var region = PreviewRegion.ForZoom(W, H, zoom, anchorX, anchorY);

            var (backX, backY) = PreviewRegion.AnchorFor(W, H, zoom, region.X, region.Y);

            Assert.Equal(anchorX, backX, 6);
            Assert.Equal(anchorY, backY, 6);
        }

        [Fact]
        public void Panning_MovesTheViewByTheImageSpaceDeltaAsked()
        {
            var (anchorX, anchorY) = PreviewRegion.PannedBy(W, H, 4d, 0.5d, 0.5d, dx: 100d, dy: -50d);
            var moved = PreviewRegion.ForZoom(W, H, 4d, anchorX, anchorY);
            var from = PreviewRegion.ForZoom(W, H, 4d, 0.5d, 0.5d);

            Assert.Equal(from.X + 100d, moved.X, 6);
            Assert.Equal(from.Y - 50d, moved.Y, 6);
        }

        [Fact]
        public void Panning_StopsAtTheImageEdge_RatherThanRunningOffIt()
        {
            var (anchorX, anchorY) = PreviewRegion.PannedBy(W, H, 4d, 0.5d, 0.5d, dx: 10_000d, dy: 10_000d);
            var region = PreviewRegion.ForZoom(W, H, 4d, anchorX, anchorY);

            Assert.Equal(W, region.X + region.Width, 6);
            Assert.Equal(H, region.Y + region.Height, 6);

            var (backX, backY) = PreviewRegion.PannedBy(W, H, 4d, 0.5d, 0.5d, dx: -10_000d, dy: -10_000d);
            var atOrigin = PreviewRegion.ForZoom(W, H, 4d, backX, backY);

            Assert.Equal(0d, atOrigin.X);
            Assert.Equal(0d, atOrigin.Y);
        }

        /// <summary>
        /// Pinch zooms about the cursor: whatever image point is under it has to stay under it, or
        /// the thing being aligned slides out from under the fingers doing the aligning.
        /// </summary>
        [Theory]
        [InlineData(0.25d, 0.25d)]
        [InlineData(0.5d, 0.5d)]
        [InlineData(0.9d, 0.1d)]
        public void ZoomingAboutAPoint_KeepsThatImagePointUnderIt(double focusU, double focusV)
        {
            const double startZoom = 2d;
            var before = PreviewRegion.ForZoom(W, H, startZoom, 0.5d, 0.5d);
            double focusImageX = before.X + (focusU * before.Width);
            double focusImageY = before.Y + (focusV * before.Height);

            var (zoom, anchorX, anchorY) = PreviewRegion.ZoomedAbout(
                W, H, startZoom, 0.5d, 0.5d, factor: 2d, focusU, focusV, minZoom: 1d, maxZoom: 32d);
            var after = PreviewRegion.ForZoom(W, H, zoom, anchorX, anchorY);

            Assert.Equal(4d, zoom, 6);
            Assert.Equal(focusImageX, after.X + (focusU * after.Width), 3);
            Assert.Equal(focusImageY, after.Y + (focusV * after.Height), 3);
        }

        [Fact]
        public void ZoomingOutPastFit_SettlesOnTheWholeImage_NotACornerOfIt()
        {
            var (zoom, anchorX, anchorY) = PreviewRegion.ZoomedAbout(
                W, H, zoom: 2d, anchorX: 0d, anchorY: 0d, factor: 0.1d, focusU: 0d, focusV: 0d,
                minZoom: 1d, maxZoom: 32d);

            Assert.Equal(1d, zoom);
            Assert.Equal(PreviewRegion.WholeImage(W, H), PreviewRegion.ForZoom(W, H, zoom, anchorX, anchorY));
        }

        [Fact]
        public void ZoomIsCappedAtTheMaximum_SoAPinchCannotRunAway()
        {
            var (zoom, _, _) = PreviewRegion.ZoomedAbout(
                W, H, zoom: 30d, anchorX: 0.5d, anchorY: 0.5d, factor: 100d, focusU: 0.5d, focusV: 0.5d,
                minZoom: 1d, maxZoom: 32d);

            Assert.Equal(32d, zoom);
        }
    }
}
