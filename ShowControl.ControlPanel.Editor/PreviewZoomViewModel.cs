using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using ShowControl.PixelMapping;

namespace ShowControl.ControlPanel.Editor
{
    /// <summary>
    /// Where the preview is looking: a magnification and the anchor the surface view is scaled
    /// about. Pure state and arithmetic — it knows nothing about views, which is what lets the
    /// whole of it be tested.
    /// <para>
    /// The view reads <see cref="Zoom"/>/<see cref="AnchorX"/>/<see cref="AnchorY"/> straight onto
    /// the video surface, and <see cref="Region"/> onto the overlay. Zoom affects presentation
    /// only: the LEDs sample the full rendered image regardless of what the operator is looking at.
    /// </para>
    /// </summary>
    public sealed partial class PreviewZoomViewModel : EditorViewModelBase
    {
        /// <summary>Whole image. There is no zooming out past the picture.</summary>
        public const double MinZoom = 1d;

        /// <summary>
        /// At the shipped 1350-wide image in a ~1330pt window one image pixel is about a point, so
        /// 32x puts a single shader pixel under a fingertip — as fine as placing a fixture needs.
        /// </summary>
        public const double MaxZoom = 32d;

        public const double ZoomStep = 1.5d;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(Region))]
        [NotifyPropertyChangedFor(nameof(Label))]
        [NotifyPropertyChangedFor(nameof(IsZoomedIn))]
        private double _zoom = MinZoom;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(Region))]
        private double _anchorX = 0.5d;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(Region))]
        private double _anchorY = 0.5d;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(Region))]
        private int _imageWidth = PixelMap.DefaultImageWidth;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(Region))]
        private int _imageHeight = PixelMap.DefaultImageHeight;

        public PreviewZoomViewModel(ILogger? logger = null) : base(logger)
        {
        }

        /// <summary>The part of the image on screen. What the overlay draws against.</summary>
        public PreviewRegion Region => PreviewRegion.ForZoom(ImageWidth, ImageHeight, Zoom, AnchorX, AnchorY);

        public string Label => Zoom <= MinZoom ? "Fit" : $"{Zoom:0.#}x";

        public bool IsZoomedIn => Zoom > MinZoom;

        /// <summary>Follows the loaded map, since a map may be authored for a different image.</summary>
        public void SetImageSize(int width, int height)
        {
            if (width <= 0 || height <= 0)
            {
                return;
            }

            ImageWidth = width;
            ImageHeight = height;
        }

        /// <summary>
        /// Zooms by <paramref name="factor"/> holding the image point under
        /// (<paramref name="focusU"/>, <paramref name="focusV"/>) — a fraction of the view — in
        /// place, so a pinch keeps what you are aiming at under the cursor.
        /// </summary>
        public void ZoomAbout(double factor, double focusU, double focusV)
        {
            if (factor <= 0d || !double.IsFinite(factor))
            {
                return;
            }

            var (zoom, anchorX, anchorY) = PreviewRegion.ZoomedAbout(
                ImageWidth, ImageHeight, Zoom, AnchorX, AnchorY, factor, focusU, focusV, MinZoom, MaxZoom);

            Zoom = zoom;
            AnchorX = anchorX;
            AnchorY = anchorY;
        }

        /// <summary>Moves the view by an image-space delta, clamped to the image.</summary>
        public void PanBy(double dx, double dy)
        {
            var (anchorX, anchorY) = PreviewRegion.PannedBy(ImageWidth, ImageHeight, Zoom, AnchorX, AnchorY, dx, dy);
            AnchorX = anchorX;
            AnchorY = anchorY;
        }

        /// <summary>Puts a region's top-left corner where asked, clamped. Used by an in-flight pan.</summary>
        public void MoveTo(double regionX, double regionY)
        {
            var (anchorX, anchorY) = PreviewRegion.AnchorFor(ImageWidth, ImageHeight, Zoom, regionX, regionY);
            AnchorX = anchorX;
            AnchorY = anchorY;
        }

        [RelayCommand]
        private void ZoomIn() => Guard("Zoom in", () => ZoomAbout(ZoomStep, 0.5d, 0.5d));

        [RelayCommand]
        private void ZoomOut() => Guard("Zoom out", () => ZoomAbout(1d / ZoomStep, 0.5d, 0.5d));

        [RelayCommand]
        private void Fit() => Guard("Fit", () =>
        {
            Zoom = MinZoom;
            AnchorX = 0.5d;
            AnchorY = 0.5d;
        });
    }
}
