using ShowControl.ControlPanel.Editor;
using ShowControl.Graphics;
using ShowControl.PixelMapping;
using System;
using System.ComponentModel;
using System.Linq;
#if WINDOWS
using ShowControl.ControlPanel.Maui.Platforms.Windows;
#endif

namespace ShowControl.ControlPanel.Maui
{
    /// <summary>
    /// The live shader preview and the pixel-map editor over it.
    /// <para>
    /// The editing itself lives in <see cref="PixelMapEditorViewModel"/>, in a project the test
    /// suite can reach. What is left here is what is genuinely platform: pointer coordinates, the
    /// drawable, the dispatcher timers, and the platform halves of the dialog interfaces.
    /// </para>
    /// <para>
    /// The panel is bound rather than driven: the switch, the panel and overlay visibility, both
    /// pickers, the file buttons, the zoom buttons and the surface's own scale and anchor are all
    /// bindings onto the view-model, and the page sets <c>x:DataType</c> so a path that does not
    /// exist there fails the build. What stays as a handler is what a binding cannot express — the
    /// pointer, the fixture list's selection (assigned only when it differs, because a redundant
    /// index-path selection crashes a list that has just changed shape), and the two pushes into
    /// non-bindable objects: the overlay drawable's region and the log scroll.
    /// </para>
    /// </summary>
    public partial class PreviewPage : ContentPage
    {
        private static readonly TimeSpan AutosaveInterval = TimeSpan.FromMilliseconds(500);

        /// <summary>Selection radius in overlay points, converted to image units at hit-test time.</summary>
        private const double HitTestRadiusInPoints = 12d;

        /// <summary>
        /// Zoom per point of scroll, applied exponentially so the feel is the same at every
        /// magnification. A trackpad flick of ~50 points is about 2x; one wheel notch is a few
        /// percent. Per-event growth is capped below so a coarse wheel cannot jump the view.
        /// </summary>
        private const double ZoomPerScrollPoint = 0.015d;

        private const double MaxZoomPerScrollEvent = 2d;

        private readonly IGraphicsHost _graphicsHost;
        private readonly PixelMapEditorViewModel _viewModel;
        private readonly IPixelMapEditor _pixelMapEditor;
        private readonly PixelMapOverlayDrawable _overlayDrawable = new PixelMapOverlayDrawable();

        private IDispatcherTimer _paramTimer;
        private IDispatcherTimer _autosaveTimer;

        private bool _suppressListSelection;

#if MACCATALYST
        /// <summary>Scroll-wheel and trackpad-scroll zoom. Held so the recognizer outlives the call.</summary>
        private ScrollZoomGesture _scrollZoom;

        /// <summary>Window lifecycle, so the preview learns it is hidden at the edge, not on a poll.</summary>
        private PreviewVisibilityMonitor _visibilityMonitor;

        /// <summary>Last reported preview visibility, so only changes are logged and pushed down.</summary>
        private bool? _lastPresentationVisible;
#endif

#if WINDOWS
        private bool _useDirectComposition = true; // Enable DirectComposition by default for XAML overlay support
#endif

        public PreviewPage(IGraphicsHost graphicsHost, PixelMapEditorViewModel viewModel, IPixelMapEditor pixelMapEditor)
        {
            InitializeComponent();
            _graphicsHost = graphicsHost;
            _viewModel = viewModel;
            _pixelMapEditor = pixelMapEditor;
            BindingContext = _viewModel;

            // Pin the native drawable to the rendered image's size, before the surface's first
            // layout pass. The preview is a full-surface stretch of that image either way, so
            // tracking the window bought nothing but a swapchain rebuild per resize step.
            vulkanSurface.PinnedDrawableWidth = _graphicsHost.Width;
            vulkanSurface.PinnedDrawableHeight = _graphicsHost.Height;

            // Wire up events from the Vulkan surface view
            vulkanSurface.SurfaceReady += OnSurfaceReady;
            vulkanSurface.SurfaceSizeChanged += OnSurfaceSizeChanged;
            vulkanSurface.SurfaceDestroying += OnSurfaceDestroying;

            _paramTimer = Dispatcher.CreateTimer();
            _paramTimer.Interval = TimeSpan.FromMilliseconds(250);
            _paramTimer.Tick += (s, e) =>
            {
                RefreshShaderParameters();

                // The backstop, not the mechanism. The notifications below are what actually catch
                // the window leaving; this is here to notice the states nobody announces — a view
                // pulled out of its window, say — and to restore presentation if an edge was missed.
                UpdatePresentationVisibility(reason: "poll");
            };
            _paramTimer.Start();

#if MACCATALYST
            _visibilityMonitor = new PreviewVisibilityMonitor();
            _visibilityMonitor.Changed += OnPreviewVisibilityNotification;
#endif

            InitializeEditor();
        }

        // ---------------------------------------------------------------- editor

        private void InitializeEditor()
        {
            MapOverlay.Drawable = _overlayDrawable;
            MapOverlay.SizeChanged += OnOverlaySizeChanged;
            MapOverlay.HandlerChanged += (s, e) => RunGuarded("Scroll zoom", AttachScrollZoom);
            AttachScrollZoom();

            _autosaveTimer = Dispatcher.CreateTimer();
            _autosaveTimer.Interval = AutosaveInterval;
            _autosaveTimer.Tick += OnAutosaveTick;

            _pixelMapEditor.Changed += OnPixelMapChangedExternally;
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
            _viewModel.Zoom.PropertyChanged += OnZoomPropertyChanged;
            _viewModel.WorkingMapReplaced += (s, e) => RefreshOverlay();
            _viewModel.Published += OnViewModelPublished;
            _viewModel.FlushRequested += OnFlushRequested;

            RefreshOverlay();
            SyncOverlayRegion();

            if (_viewModel.RecoveredUnsavedWork)
            {
                // The prompt the user did not get on the way out. Deferred so the window is up.
                Dispatcher.Dispatch(() => _viewModel.ReviewRecoveredWorkCommand.Execute(null));
            }
        }

#if MACCATALYST
        /// <summary>
        /// A window-lifecycle notification arrived. Re-evaluates immediately rather than leaving it
        /// to the poll, which the system slows down at exactly the moment this matters.
        /// </summary>
        private void OnPreviewVisibilityNotification(object sender, PreviewVisibilityEventArgs e) =>
            RunGuarded("Preview visibility", () =>
            {
                // Window notifications are posted for every window in the process, and MAUI has more
                // than one. Only this preview's window says anything about this preview.
                if (e.Source is UIKit.UIWindow window &&
                    vulkanSurface.Handler?.PlatformView is UIKit.UIView view &&
                    !ReferenceEquals(window, view.Window))
                {
                    return;
                }

                // A leaving notification is acted on, not verified: it is delivered before the state
                // it announces is readable, so asking would get the answer that is about to be wrong.
                UpdatePresentationVisibility(e.HidesPreview ? false : null, e.Notification);
            });
#endif

        /// <summary>
        /// Tells the graphics host whether the preview is actually on screen.
        /// <para>
        /// A hidden window withholds its drawable, and a frame submitted for presentation waits on
        /// that drawable — with the rig's pixels in the same submission. So this is not about saving
        /// work while nobody is looking; it is what keeps a minimised window from stopping the
        /// lights.
        /// </para>
        /// <para>
        /// Called from the UI thread, which is the only place UIKit may be asked, and from two
        /// directions: the lifecycle notifications, which are the real signal, and a 250ms poll that
        /// backstops them. <paramref name="forced"/> is how a notification reports a transition the
        /// state has not caught up with yet; everything else re-derives from UIKit, so there is only
        /// ever one definition of visible.
        /// </para>
        /// <para>
        /// The raw signals are logged on every change: which of them actually moves when a Catalyst
        /// window is minimised is worth having on the record rather than assumed.
        /// </para>
        /// </summary>
        /// <param name="forced">Overrides the derived answer; only ever false, never true.</param>
        /// <param name="reason">What prompted the check, for the log.</param>
        private void UpdatePresentationVisibility(bool? forced = null, string reason = "poll")
        {
#if MACCATALYST
            if (vulkanSurface.Handler?.PlatformView is not UIKit.UIView view)
            {
                return;
            }

            var window = view.Window;
            var scene = window?.WindowScene;
            var state = scene?.ActivationState;

            // Nothing short of foreground-*active* is safe to present into. Foreground-inactive is
            // not "visible but unfocused" as far as Core Animation is concerned: it is also the state
            // of a window that is minimised, on another Space, or completely covered by another app,
            // and in all of those the compositor stops recycling drawables — which strands the frame
            // the rig's pixels are travelling in. The trade is that the preview holds its last frame
            // whenever the app is not frontmost. That is the right way round for a show rig: the
            // picture comes back the moment the window is clicked, and the lights never stopped.
            bool visible =
                forced ??
                (window != null &&
                 !window.Hidden &&
                 !view.Hidden &&
                 view.Superview != null &&
                 state == UIKit.UISceneActivationState.ForegroundActive);

            if (_lastPresentationVisible == visible)
            {
                return;
            }

            _lastPresentationVisible = visible;
            Serilog.Log.Debug(
                "[PreviewPage] Preview visibility → {Visible} via {Reason} (window={HasWindow}, windowHidden={WindowHidden}, viewHidden={ViewHidden}, superview={HasSuperview}, sceneState={State})",
                visible, reason, window != null, window?.Hidden, view.Hidden, view.Superview != null, state);

            _graphicsHost.SetPresentationEnabled(visible);
#endif
        }

        /// <summary>Hands the drawable the current map, selection and grabbed pixel, and redraws.</summary>
        private void RefreshOverlay()
        {
            _overlayDrawable.Map = _viewModel.Working;
            _overlayDrawable.SelectedFixtureId = _viewModel.SelectedFixtureId;
            _overlayDrawable.DraggedPixelIndex = _viewModel.DraggedPixelIndex;
            MapOverlay.Invalidate();
        }

        private void OnViewModelPropertyChanged(object sender, PropertyChangedEventArgs e) =>
            RunGuarded("Editor update", () =>
            {
                if (e.PropertyName == nameof(PixelMapEditorViewModel.SelectedFixtureId))
                {
                    _overlayDrawable.SelectedFixtureId = _viewModel.SelectedFixtureId;
                    SyncListSelection();
                    MapOverlay.Invalidate();
                }
            });

        /// <summary>
        /// The autosave is paced here rather than in the view-model, which has no dispatcher and no
        /// business owning a timer. Every publish — including each move of a drag — pushes the
        /// deadline out, so the file is written once the operator stops rather than per frame.
        /// </summary>
        private void OnViewModelPublished(object sender, EventArgs e)
        {
            _autosaveTimer.Stop();
            _autosaveTimer.Start();

            // Anything published may have moved a pixel, so this is the one place the overlay needs
            // redrawing — a drag, a respace, a flip and an added fixture all arrive here.
            MapOverlay.Invalidate();
        }

        private void OnPixelMapChangedExternally(object sender, PixelMapChangedEventArgs e) =>
            MainThread.BeginInvokeOnMainThread(() => RunGuarded("Pixel map update", _viewModel.HandleExternalChange));

        private void OnAutosaveTick(object sender, EventArgs e) => RunGuarded("Autosave", () =>
        {
            _autosaveTimer.Stop();
            _viewModel.SaveWorkingCopy();
        });

        private void OnFlushRequested(object sender, EventArgs e) =>
            RunGuarded("Autosave", FlushWorkingCopy);

        /// <summary>Flushes pending edits immediately. Used on the paths where there is no later chance.</summary>
        public void FlushWorkingCopy()
        {
            _autosaveTimer?.Stop();
            _viewModel.SaveWorkingCopy();
        }

        /// <summary>
        /// Runs a UI event handler so a fault reaches the log and the status line instead of the
        /// process. On Mac Catalyst an exception leaving a handler is marshalled straight into an
        /// Objective-C exception, which aborts — a rig mid-show must not die because the editor
        /// panel tripped over something. Command bodies are guarded inside the view-model, which
        /// bindings would otherwise bypass entirely.
        /// </summary>
        private void RunGuarded(string what, Action action)
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                Serilog.Log.Error(ex, "[PreviewPage] {What} failed.", what);
                try
                {
                    _viewModel.StatusText = $"{what} failed: {ex.Message}";
                }
                catch (Exception statusEx)
                {
                    Serilog.Log.Error(statusEx, "[PreviewPage] Could not show the failure on the status line.");
                }
            }
        }

        // ---------------------------------------------------------------- zoom

        /// <summary>
        /// The half of the zoom that bindings cannot do. The video is magnified by scaling the
        /// surface view about the anchor, and that is bound straight to the view-model in XAML — a
        /// compositing transform, so the view's bounds are unchanged and no swapchain is rebuilt.
        /// The overlay is not scaled at all: it is redrawn from the equivalent region, which is what
        /// keeps its dots and lines vector-crisp and its grab radius constant on screen. A drawable
        /// has no bindable properties, so that push is here.
        /// <para>
        /// The two are derived independently from the same state, so an error in
        /// <see cref="PreviewRegion"/> shows up as the map drifting off the picture. That is the
        /// check to run after touching any of this.
        /// </para>
        /// </summary>
        private void SyncOverlayRegion()
        {
            _overlayDrawable.Region = _viewModel.Zoom.Region;
            MapOverlay.Invalidate();
        }

        /// <summary>
        /// Every route to a new zoom — the buttons, scroll, pinch, a pan, edit mode resetting it —
        /// ends at the same property change, so the overlay follows from here rather than from a
        /// call after each one.
        /// </summary>
        private void OnZoomPropertyChanged(object sender, PropertyChangedEventArgs e) =>
            RunGuarded("Zoom", () =>
            {
                if (e.PropertyName == nameof(PreviewZoomViewModel.Region))
                {
                    SyncOverlayRegion();
                }
            });

        /// <summary>
        /// Attaches scroll-to-zoom to the overlay's platform view. MAUI surfaces no wheel event, so
        /// this is native: see <c>ScrollZoomGesture</c>. Re-run whenever the handler changes, since
        /// the platform view is recreated with it, and a no-op on platforms without an
        /// implementation — the buttons and pinch still cover zooming there.
        /// </summary>
        private void AttachScrollZoom()
        {
#if MACCATALYST
            if (MapOverlay.Handler?.PlatformView is not UIKit.UIView platformView)
            {
                return;
            }

            _scrollZoom?.Dispose();
            _scrollZoom = new ScrollZoomGesture(platformView);
            _scrollZoom.Scrolled += OnScrollZoom;
#endif
        }

        /// <summary>
        /// Scroll wheel or two-finger scroll, zooming about the cursor. Scrolling away from you
        /// zooms in, which is the convention everywhere else; if it ever feels inverted the sign
        /// here is the only thing to change.
        /// <para>
        /// Ignored while something else owns the gesture — a fixture drag or a pan — so a stray
        /// scroll mid-drag cannot move the ground under it.
        /// </para>
        /// </summary>
        private void OnScrollZoom(object sender, ScrollZoomEventArgs e) => RunGuarded("Zoom", () =>
        {
            if (_viewModel.IsDragging || _viewModel.IsPanning)
            {
                return;
            }

            // Negated: UIKit reports a scroll as the translation of the content being dragged, so a
            // scroll away from the user comes through negative, and that is the one that zooms in.
            double factor = Math.Exp(-e.DeltaY * ZoomPerScrollPoint);
            factor = Math.Clamp(factor, 1d / MaxZoomPerScrollEvent, MaxZoomPerScrollEvent);

            _viewModel.Zoom.ZoomAbout(factor, e.FocusU, e.FocusV);
        });

        /// <summary>
        /// Trackpad pinch, zooming about the point under the cursor so what you are aiming at stays
        /// where it is. Ignored mid-drag: the pointer owns the gesture then, and Mac Catalyst
        /// delivers pointer moves during a pinch.
        /// </summary>
        private void OnOverlayPinch(object sender, PinchGestureUpdatedEventArgs e) => RunGuarded("Zoom", () =>
        {
            if (_viewModel.IsDragging)
            {
                return;
            }

            if (e.Status == GestureStatus.Started)
            {
                _viewModel.EndPan();
                return;
            }

            if (e.Status != GestureStatus.Running || e.Scale <= 0d)
            {
                return;
            }

            _viewModel.Zoom.ZoomAbout(e.Scale, e.ScaleOrigin.X, e.ScaleOrigin.Y);
        });

        // ---------------------------------------------------------------- pointer

        private PixelMapOverlayTransform CurrentTransform() => new PixelMapOverlayTransform(
            _viewModel.Working, MapOverlay.Width, MapOverlay.Height, _viewModel.Zoom.Region);

        private void OnOverlayPointerPressed(object sender, PointerEventArgs e) =>
            RunGuarded("Fixture selection", () => HandlePointerPressed(e));

        private void HandlePointerPressed(PointerEventArgs e)
        {
            var transform = CurrentTransform();
            if (!transform.IsUsable)
            {
                return;
            }

            var point = e.GetPosition(MapOverlay);
            if (point == null)
            {
                return;
            }

            var (imageX, imageY) = transform.ToImage(point.Value.X, point.Value.Y);
            double threshold = HitTestRadiusInPoints * transform.ImageUnitsPerOverlayPoint;

            // Grabs the fixture under the point, or starts a pan if there is none. Which of the two
            // is running is the view-model's to know; the view only supplies the coordinates.
            _viewModel.BeginGesture(imageX, imageY, threshold);

            SyncListSelection();
            RefreshOverlay();
        }

        private void OnOverlayPointerMoved(object sender, PointerEventArgs e)
        {
            try
            {
                HandlePointerMoved(e);
            }
            catch (Exception ex)
            {
                // Ending both matters as much as the log line: this handler runs at pointer rate, so
                // a fault left running would repeat until the log is useless.
                _viewModel.EndGesture();
                Serilog.Log.Error(ex, "[PreviewPage] Pointer drag failed.");
                _viewModel.StatusText = "Drag failed: " + ex.Message;
            }
        }

        /// <summary>
        /// Moves the fixture, or the view. A fixture drag publishes on every move — that is what
        /// makes the pixels the shader animation reads follow the cursor — and only the overlay is
        /// redrawn here; the panel catches up when the drag ends.
        /// </summary>
        private void HandlePointerMoved(PointerEventArgs e)
        {
            if (_viewModel.IsPanning)
            {
                PanTo(e);
                return;
            }

            if (!_viewModel.IsDragging)
            {
                return;
            }

            var transform = CurrentTransform();
            if (!transform.IsUsable)
            {
                return;
            }

            var point = e.GetPosition(MapOverlay);
            if (point == null)
            {
                return;
            }

            var (imageX, imageY) = transform.ToImage(point.Value.X, point.Value.Y);

            // The redraw comes from the view-model's Published event, so a move that changes nothing
            // costs nothing.
            _viewModel.DragTo(imageX, imageY);
        }

        /// <summary>
        /// Moves the view with the cursor. Converts through the region captured at the press, so the
        /// grabbed image point stays under the pointer however far the view has already moved — the
        /// same anchoring the fixture drag uses, for the same reason. A pan is not an edit: nothing
        /// is published, nothing is autosaved, the map is not touched.
        /// </summary>
        private void PanTo(PointerEventArgs e)
        {
            var point = e.GetPosition(MapOverlay);
            if (point == null || MapOverlay.Width <= 0 || MapOverlay.Height <= 0)
            {
                return;
            }

            var pressTransform = new PixelMapOverlayTransform(
                _viewModel.Working, MapOverlay.Width, MapOverlay.Height, _viewModel.PanStartRegion);
            var (imageX, imageY) = pressTransform.ToImage(point.Value.X, point.Value.Y);

            _viewModel.PanTo(imageX, imageY);
        }

        private void OnOverlayPointerReleased(object sender, PointerEventArgs e) =>
            RunGuarded("Drop", EndPointerGesture);

        /// <summary>
        /// A drag that leaves the overlay ends here. Mac Catalyst reports the exit and then finishes
        /// the gesture without a PointerReleased, while hover moves keep arriving — a drag left
        /// running would then follow the cursor with no button held.
        /// </summary>
        private void OnOverlayPointerExited(object sender, PointerEventArgs e) =>
            RunGuarded("Drop", EndPointerGesture);

        private void EndPointerGesture()
        {
            // The flush a finished drag needs comes back as FlushRequested; the view-model asks for
            // it because it is the one that knows an edit happened.
            if (_viewModel.EndGesture())
            {
                SyncListSelection();
                RefreshOverlay();
            }
        }

        // ---------------------------------------------------------------- resize

        /// <summary>
        /// The map is in shader-image space and the preview is a full-surface stretch of that fixed
        /// offscreen image, so a resize never touches the map — the overlay just recomputes its
        /// scale from its own bounds. The zoom needs nothing either: the visible region is a
        /// fraction of the image and the anchor is relative to the view, so the same part stays on
        /// screen.
        /// <para>
        /// Nothing dims here any more. The overlay used to fade over the preview during a resize
        /// because presentation was suspended while the swapchain was rebuilt, leaving a stale frame
        /// the map no longer matched. With the drawable pinned the preview keeps presenting
        /// throughout and the compositor scales it, so the two stay registered the whole way.
        /// </para>
        /// </summary>
        private void OnOverlaySizeChanged(object sender, EventArgs e)
        {
            _viewModel.EndGesture();
            RefreshOverlay();
        }

        // ---------------------------------------------------------------- fixtures

        /// <summary>
        /// Only assigns when the selection actually differs: every set reaches UICollectionView as a
        /// select/deselect against an index path, and a redundant one is a crash waiting for a list
        /// that has just changed shape.
        /// </summary>
        private void SyncListSelection()
        {
            var target = _viewModel.SelectedFixtureId.HasValue
                ? _viewModel.Rows.FirstOrDefault(r => r.Id == _viewModel.SelectedFixtureId.Value)
                : null;

            if (Equals(FixtureList.SelectedItem, target))
            {
                return;
            }

            _suppressListSelection = true;
            try
            {
                FixtureList.SelectedItem = target;
            }
            finally
            {
                _suppressListSelection = false;
            }
        }

        private void OnFixtureSelected(object sender, SelectionChangedEventArgs e) =>
            RunGuarded("Fixture selection", () =>
            {
                // Selection changes the list raises back at us — a refresh replacing a row, or the
                // platform clearing its selection afterwards — must not overwrite what the pointer
                // is holding. While a drag is running the pointer owns the selection outright.
                if (_suppressListSelection || _viewModel.IsDragging)
                {
                    return;
                }

                _viewModel.SelectedFixtureId = (e.CurrentSelection.FirstOrDefault() as FixtureRow)?.Id;
            });

        // ---------------------------------------------------------------- surface

        /// <summary>
        /// Polls the knobs and hands them over. What each one is called depends on the running
        /// shader, and that mapping is the view-model's — this reads the host and nothing else.
        /// The readout only notifies when a value actually moved, so a still panel costs no layout.
        /// </summary>
        private void RefreshShaderParameters() => _viewModel.ShaderParameters.Update(
            _graphicsHost.CurrentShader,
            _graphicsHost.DistortionStrength,
            _graphicsHost.ShaderParam1,
            _graphicsHost.ShaderParam2,
            _graphicsHost.ShaderParam3,
            _graphicsHost.ShaderParam4);

        private void OnSurfaceReady(object sender, nint surfaceHandle)
        {
            // The surface lifecycle goes to the real log, not just the debugger: what the window
            // does on minimise, hide and Space changes is only diagnosable after the fact.
            Serilog.Log.Debug("[PreviewPage] Surface ready (layer {Handle:X})", surfaceHandle);
#if MACCATALYST
            // The pinned drawable size, not the view's: the swapchain has to match the layer's
            // drawable exactly, and that is now fixed at the rendered image's size.
            _graphicsHost.EnableMetalPresentation(surfaceHandle, (uint)_graphicsHost.Width, (uint)_graphicsHost.Height);
#elif WINDOWS
            if (_useDirectComposition && vulkanSurface.Handler is SwapChainPanelHandler swapChainHandler)
            {
                // DirectComposition mode: use Vulkan/D3D11 interop for XAML overlay support
                var d3d11Host = swapChainHandler.D3D11Host;
                if (d3d11Host != null)
                {
                    // Use D3D11Host's swap chain dimensions to ensure shared texture matches
                    var width = d3d11Host.Width;
                    var height = d3d11Host.Height;
                    System.Diagnostics.Debug.WriteLine($"[PreviewPage] Using DirectComposition mode at {width}x{height} (from D3D11Host)");
                    _graphicsHost.EnableDirectCompositionPresentation(d3d11Host, width, height);
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("[PreviewPage] D3D11Host is null, falling back to Win32 mode");
                    _graphicsHost.EnableWin32Presentation(surfaceHandle, (uint)vulkanSurface.Width, (uint)vulkanSurface.Height);
                }
            }
            else
            {
                // Win32 mode: direct Vulkan presentation (no XAML overlay support)
                System.Diagnostics.Debug.WriteLine("[PreviewPage] Using Win32 mode");
                _graphicsHost.EnableWin32Presentation(surfaceHandle, (uint)vulkanSurface.Width, (uint)vulkanSurface.Height);
            }
#endif
        }

        private void OnSurfaceSizeChanged(object sender, SurfaceSizeChangedEventArgs e)
        {
            Serilog.Log.Debug("[PreviewPage] Surface size changed: {Width}x{Height}", e.Width, e.Height);
#if MACCATALYST
            _graphicsHost.HandleMetalSurfaceResize((uint)e.Width, (uint)e.Height);
#elif WINDOWS
            if (_useDirectComposition && vulkanSurface.Handler is SwapChainPanelHandler)
            {
                _graphicsHost.HandleDirectCompositionResize((uint)e.Width, (uint)e.Height);
            }
            else
            {
                _graphicsHost.HandleWin32SurfaceResize((uint)e.Width, (uint)e.Height);
            }
#endif
        }

        private void OnSurfaceDestroying(object sender, EventArgs e)
        {
            Serilog.Log.Debug("[PreviewPage] Surface destroying (view left its window)");
        }

        protected override void OnDisappearing()
        {
            base.OnDisappearing();
            _paramTimer?.Stop();

            // Last chance on this window: the map must be on disk before it goes away.
            FlushWorkingCopy();
            _pixelMapEditor.Changed -= OnPixelMapChangedExternally;
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
            _viewModel.Zoom.PropertyChanged -= OnZoomPropertyChanged;
            _viewModel.Published -= OnViewModelPublished;
            _viewModel.FlushRequested -= OnFlushRequested;

#if MACCATALYST
            _scrollZoom?.Dispose();
            _scrollZoom = null;

            if (_visibilityMonitor != null)
            {
                _visibilityMonitor.Changed -= OnPreviewVisibilityNotification;
                _visibilityMonitor.Dispose();
                _visibilityMonitor = null;
            }
#endif

            Serilog.Log.Debug("[PreviewPage] Disappearing; tearing down presentation.");
            _graphicsHost.OnPreviewWindowClosed();
        }
    }
}
