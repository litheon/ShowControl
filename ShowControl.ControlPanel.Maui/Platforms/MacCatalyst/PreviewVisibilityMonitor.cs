using System;
using System.Collections.Generic;
using Foundation;
using UIKit;

namespace ShowControl.ControlPanel.Maui
{
    /// <summary>
    /// A window-lifecycle notification, and whether it is on its own reason enough to stop
    /// presenting.
    /// </summary>
    public sealed class PreviewVisibilityEventArgs : EventArgs
    {
        public PreviewVisibilityEventArgs(string notification, NSObject source, bool hidesPreview)
        {
            Notification = notification;
            Source = source;
            HidesPreview = hidesPreview;
        }

        /// <summary>The notification's name, for the log — this is a path worth being able to read.</summary>
        public string Notification { get; }

        /// <summary>
        /// What the notification was about: a <see cref="UIWindow"/>, a <see cref="UIScene"/> or the
        /// application. Window notifications are raised for every window in the process, so a
        /// listener that cares about one window has to check.
        /// </summary>
        public NSObject Source { get; }

        /// <summary>
        /// True for the notifications that fire *before* the state they announce is readable. The
        /// scene is still <c>ForegroundActive</c> inside <c>WillDeactivate</c>, so acting on the
        /// notification is the whole point of listening for it — polling the state at that moment
        /// gives the answer that is about to be wrong.
        /// </summary>
        public bool HidesPreview { get; }
    }

    /// <summary>
    /// Watches the scene, application and window lifecycle so the preview learns it has gone off
    /// screen at the moment it happens, rather than whenever a timer next gets around to asking.
    /// <para>
    /// The distinction matters because the timer is the thing least able to help here: a MAUI
    /// dispatcher timer is an <c>NSTimer</c> on the main run loop, and the system coalesces those
    /// precisely when the app stops being frontmost — so the poll slows down at the moment its
    /// answer becomes urgent. These notifications are delivered regardless.
    /// </para>
    /// <para>
    /// The monitor deliberately does not decide anything. It says "something happened, and here is
    /// whether it is a leaving-kind of something"; the page re-evaluates its own definition of
    /// visible, so there is exactly one of those.
    /// </para>
    /// </summary>
    public sealed class PreviewVisibilityMonitor : IDisposable
    {
        private readonly List<NSObject> _observers = new List<NSObject>();
        private bool _disposed;

        public PreviewVisibilityMonitor()
        {
            // Leaving. Acted on at the edge: by the time the state catches up, a frame has been
            // submitted against a drawable the compositor is no longer handing back.
            Observe(UIScene.WillDeactivateNotification, hidesPreview: true);
            Observe(UIScene.DidEnterBackgroundNotification, hidesPreview: true);
            Observe(UIApplication.WillResignActiveNotification, hidesPreview: true);
            Observe(UIApplication.DidEnterBackgroundNotification, hidesPreview: true);
            Observe(UIWindow.DidBecomeHiddenNotification, hidesPreview: true);

            // Returning. Only a prompt to re-evaluate, never taken as proof: coming back to the
            // foreground says nothing about whether this particular view is in a window yet.
            Observe(UIScene.DidActivateNotification, hidesPreview: false);
            Observe(UIScene.WillEnterForegroundNotification, hidesPreview: false);
            Observe(UIApplication.DidBecomeActiveNotification, hidesPreview: false);
            Observe(UIApplication.WillEnterForegroundNotification, hidesPreview: false);
            Observe(UIWindow.DidBecomeVisibleNotification, hidesPreview: false);
        }

        /// <summary>Raised on the main thread, which is where UIKit posts these.</summary>
        public event EventHandler<PreviewVisibilityEventArgs>? Changed;

        private void Observe(NSString name, bool hidesPreview)
        {
            _observers.Add(NSNotificationCenter.DefaultCenter.AddObserver(
                name,
                notification => Changed?.Invoke(
                    this,
                    new PreviewVisibilityEventArgs(
                        notification.Name ?? name.ToString(),
                        notification.Object,
                        hidesPreview))));
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            foreach (var observer in _observers)
            {
                NSNotificationCenter.DefaultCenter.RemoveObserver(observer);
                observer.Dispose();
            }

            _observers.Clear();
        }
    }
}
