using System;
using CoreGraphics;
using Foundation;
using UIKit;

namespace ShowControl.ControlPanel.Maui
{
    /// <summary>
    /// Where a scroll landed and how far it went, in the view's own coordinates.
    /// </summary>
    public sealed class ScrollZoomEventArgs : EventArgs
    {
        public ScrollZoomEventArgs(double deltaY, double focusU, double focusV)
        {
            DeltaY = deltaY;
            FocusU = focusU;
            FocusV = focusV;
        }

        /// <summary>
        /// Points of scroll since the last event, in UIKit's pan convention: positive as the content
        /// is dragged downward, which is a scroll towards the user. It already reflects the system's
        /// natural-scrolling preference, so anything derived from it follows the same direction as
        /// scrolling everywhere else on the machine.
        /// </summary>
        public double DeltaY { get; }

        /// <summary>Cursor position as a fraction of the view, which is what the zoom focuses on.</summary>
        public double FocusU { get; }

        public double FocusV { get; }
    }

    /// <summary>
    /// Scroll-wheel and two-finger trackpad scroll, which MAUI does not surface at all.
    /// <para>
    /// Mac Catalyst delivers both as <em>indirect scroll</em> events to a
    /// <see cref="UIPanGestureRecognizer"/> whose <c>AllowedScrollTypesMask</c> asks for them. The
    /// recognizer is restricted to scroll alone by allowing no touch types: without that it would
    /// also claim ordinary click-drags and fight the pointer gestures that move fixtures and pan the
    /// view.
    /// </para>
    /// </summary>
    public sealed class ScrollZoomGesture : IDisposable
    {
        private readonly UIView _view;
        private readonly UIPanGestureRecognizer _recognizer;
        private nfloat _lastTranslationY;
        private bool _disposed;

        public ScrollZoomGesture(UIView view)
        {
            _view = view ?? throw new ArgumentNullException(nameof(view));

            _recognizer = new UIPanGestureRecognizer(OnPan)
            {
                // Scroll only. An empty set of touch types is what turns a pan recognizer into a
                // scroll recognizer rather than one that also swallows drags.
                AllowedTouchTypes = Array.Empty<NSNumber>(),
                AllowedScrollTypesMask = UIScrollTypeMask.All,
                ShouldRecognizeSimultaneously = (g, other) => true
            };

            _view.AddGestureRecognizer(_recognizer);
        }

        /// <summary>Raised for each scroll increment while the pointer is over the view.</summary>
        public event EventHandler<ScrollZoomEventArgs>? Scrolled;

        private void OnPan(UIPanGestureRecognizer recognizer)
        {
            if (recognizer.State == UIGestureRecognizerState.Began)
            {
                _lastTranslationY = 0;
                return;
            }

            if (recognizer.State != UIGestureRecognizerState.Changed)
            {
                return;
            }

            var translation = recognizer.TranslationInView(_view);
            nfloat delta = translation.Y - _lastTranslationY;
            _lastTranslationY = translation.Y;

            var bounds = _view.Bounds;
            if (bounds.Width <= 0 || bounds.Height <= 0 || delta == 0)
            {
                return;
            }

            CGPoint location = recognizer.LocationInView(_view);
            Scrolled?.Invoke(this, new ScrollZoomEventArgs(
                delta,
                location.X / bounds.Width,
                location.Y / bounds.Height));
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _view.RemoveGestureRecognizer(_recognizer);
            _recognizer.Dispose();
        }
    }
}
