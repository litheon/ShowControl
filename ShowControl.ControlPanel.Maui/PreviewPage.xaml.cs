using ShowControl.Graphics;
using ShowControl.PixelMapping;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
#if WINDOWS
using ShowControl.ControlPanel.Maui.Platforms.Windows;
#endif

namespace ShowControl.ControlPanel.Maui
{
    public partial class PreviewPage : ContentPage
    {
        /// <summary>
        /// Long enough to outlast the graphics host's own 100ms resize debounce, after which the
        /// swapchain has been rebuilt and the preview is being presented again.
        /// </summary>
        private static readonly TimeSpan ResizeSettleInterval = TimeSpan.FromMilliseconds(180);

        private static readonly TimeSpan AutosaveInterval = TimeSpan.FromMilliseconds(500);

        /// <summary>Selection radius in overlay points, converted to image units at hit-test time.</summary>
        private const double HitTestRadiusInPoints = 12d;

        private readonly IGraphicsHost _graphicsHost;
        private readonly IPixelMapEditor _pixelMapEditor;
        private readonly IPixelMapFileDialog _fileDialog = new PixelMapFileDialog();
        private readonly PixelMapOverlayDrawable _overlayDrawable = new PixelMapOverlayDrawable();
        private readonly ObservableCollection<FixtureRow> _fixtureRows = new ObservableCollection<FixtureRow>();

        private IDispatcherTimer _paramTimer;
        private IDispatcherTimer _resizeSettleTimer;
        private IDispatcherTimer _autosaveTimer;

        /// <summary>The map being edited. Applied to the session, which clones before publishing.</summary>
        private PixelMap _working;
        private Guid? _selectedFixtureId;
        private bool _applyingOwnEdit;
        private bool _dragging;
        private double _dragLastImageX;
        private double _dragLastImageY;

#if WINDOWS
        private bool _useDirectComposition = true; // Enable DirectComposition by default for XAML overlay support
#endif

        public PreviewPage(IGraphicsHost graphicsHost, IPixelMapEditor pixelMapEditor)
        {
            InitializeComponent();
            _graphicsHost = graphicsHost;
            _pixelMapEditor = pixelMapEditor;

            // Wire up events from the Vulkan surface view
            vulkanSurface.SurfaceReady += OnSurfaceReady;
            vulkanSurface.SurfaceSizeChanged += OnSurfaceSizeChanged;
            vulkanSurface.SurfaceDestroying += OnSurfaceDestroying;

            _paramTimer = Dispatcher.CreateTimer();
            _paramTimer.Interval = TimeSpan.FromMilliseconds(250);
            _paramTimer.Tick += (s, e) => UpdateShaderParamsLabel();
            _paramTimer.Start();

            InitializeEditor();
        }

        // ---------------------------------------------------------------- editor

        private void InitializeEditor()
        {
            MapOverlay.Drawable = _overlayDrawable;
            MapOverlay.SizeChanged += OnOverlaySizeChanged;
            FixtureList.ItemsSource = _fixtureRows;

            NewInterface.ItemsSource = Enum.GetNames(typeof(OutputInterfaceType));
            NewInterface.SelectedIndex = 0;
            NewPixelType.ItemsSource = PixelTypeNames();
            NewPixelType.SelectedIndex = Math.Max(0, PixelTypeNames().IndexOf("WS2812B"));

            _autosaveTimer = Dispatcher.CreateTimer();
            _autosaveTimer.Interval = AutosaveInterval;
            _autosaveTimer.Tick += OnAutosaveTick;

            _resizeSettleTimer = Dispatcher.CreateTimer();
            _resizeSettleTimer.Interval = ResizeSettleInterval;
            _resizeSettleTimer.Tick += OnResizeSettled;

            _pixelMapEditor.Changed += OnPixelMapChangedExternally;

            AdoptCurrentMap();

            if (_pixelMapEditor.RecoveredUnsavedWork)
            {
                // The prompt the user did not get on the way out. Deferred so the window is up.
                Dispatcher.Dispatch(async () => await PromptForRecoveredWork());
            }
        }

        private static List<string> PixelTypeNames() => new List<string>
        {
            "WS2812B", "AsteraAX3", "AsteraTitan", "BetopperDJ", "BetopperDJNew",
            "ChauvetFreedom", "JLPow", "Missyee", "Sheds"
        };

        private void AdoptCurrentMap()
        {
            _working = _pixelMapEditor.Current.Clone();
            if (_selectedFixtureId.HasValue && _working.Fixtures.All(f => f.Id != _selectedFixtureId.Value))
            {
                _selectedFixtureId = null;
            }
            RefreshEditorUi();
        }

        private void OnPixelMapChangedExternally(object sender, PixelMapChangedEventArgs e)
        {
            // Apply clones before publishing, so the published map is never our working instance
            // and reference equality cannot distinguish our own edits. The flag does: without it
            // every pointer move would re-clone the map and rebuild the fixture list mid-drag.
            if (_applyingOwnEdit)
            {
                return;
            }

            MainThread.BeginInvokeOnMainThread(AdoptCurrentMap);
        }

        private void RefreshEditorUi()
        {
            _overlayDrawable.Map = _working;
            _overlayDrawable.SelectedFixtureId = _selectedFixtureId;
            MapOverlay.Invalidate();

            _fixtureRows.Clear();
            foreach (var fixture in _working.Fixtures)
            {
                _fixtureRows.Add(FixtureRow.From(fixture));
            }

            DirtyLabel.Text = _pixelMapEditor.IsDirty
                ? "Unsaved changes" + (_pixelMapEditor.SavedPath == null ? "" : " · " + _pixelMapEditor.SavedPath)
                : _pixelMapEditor.SavedPath ?? "Built-in default map";
        }

        /// <summary>Validates and publishes the working map, then refreshes the UI and kicks the autosave.</summary>
        private bool PublishWorkingMap()
        {
            IReadOnlyList<string> problems;
            _applyingOwnEdit = true;
            try
            {
                problems = _pixelMapEditor.Apply(_working);
            }
            finally
            {
                _applyingOwnEdit = false;
            }

            if (problems.Count > 0)
            {
                StatusLabel.Text = string.Join(Environment.NewLine, problems);
                return false;
            }

            StatusLabel.Text = string.Empty;
            RefreshEditorUi();

            _autosaveTimer.Stop();
            _autosaveTimer.Start();
            return true;
        }

        private void OnAutosaveTick(object sender, EventArgs e)
        {
            _autosaveTimer.Stop();
            _pixelMapEditor.SaveWorkingCopy();
        }

        /// <summary>Flushes pending edits immediately. Used on the paths where there is no later chance.</summary>
        public void FlushWorkingCopy()
        {
            _autosaveTimer?.Stop();
            _pixelMapEditor.SaveWorkingCopy();
        }

        private void OnEditModeToggled(object sender, ToggledEventArgs e)
        {
            bool editing = e.Value;
            MapOverlay.IsVisible = editing;
            MapOverlay.InputTransparent = !editing;
            EditorPanel.IsVisible = editing;

            if (editing)
            {
                AdoptCurrentMap();
            }
            else
            {
                _dragging = false;
                FlushWorkingCopy();
            }
        }

        // ---------------------------------------------------------------- pointer

        private PixelMapOverlayTransform CurrentTransform() =>
            new PixelMapOverlayTransform(_working, MapOverlay.Width, MapOverlay.Height);

        private void OnOverlayPointerPressed(object sender, PointerEventArgs e)
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

            var hit = HitTest(imageX, imageY, threshold);
            _selectedFixtureId = hit?.Id;
            SyncListSelection();

            if (hit != null)
            {
                _dragging = true;
                _dragLastImageX = imageX;
                _dragLastImageY = imageY;
            }

            _overlayDrawable.SelectedFixtureId = _selectedFixtureId;
            MapOverlay.Invalidate();
        }

        private void OnOverlayPointerMoved(object sender, PointerEventArgs e)
        {
            if (!_dragging || !_selectedFixtureId.HasValue)
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
            var fixture = _working.Fixtures.FirstOrDefault(f => f.Id == _selectedFixtureId.Value);
            if (fixture == null)
            {
                _dragging = false;
                return;
            }

            // Dragging moves the origin only. The per-pixel offsets are what give the fixture its
            // shape and they are deliberately untouched here.
            fixture.OriginX += (float)(imageX - _dragLastImageX);
            fixture.OriginY += (float)(imageY - _dragLastImageY);
            _dragLastImageX = imageX;
            _dragLastImageY = imageY;

            PublishWorkingMap();
        }

        private void OnOverlayPointerReleased(object sender, PointerEventArgs e)
        {
            if (_dragging)
            {
                _dragging = false;
                FlushWorkingCopy();
            }
        }

        private FixtureDefinition HitTest(double imageX, double imageY, double threshold)
        {
            FixtureDefinition best = null;
            double bestDistanceSquared = threshold * threshold;

            foreach (var fixture in _working.Fixtures)
            {
                for (int i = 0; i < fixture.PixelCount; i++)
                {
                    var position = PixelMapGeometry.Resolve(fixture, i);
                    double dx = position.X - imageX;
                    double dy = position.Y - imageY;
                    double distanceSquared = (dx * dx) + (dy * dy);
                    if (distanceSquared <= bestDistanceSquared)
                    {
                        bestDistanceSquared = distanceSquared;
                        best = fixture;
                    }
                }
            }

            return best;
        }

        // ---------------------------------------------------------------- resize

        /// <summary>
        /// The map is in shader-image space and the preview is a full-surface stretch of a fixed
        /// 1080x960 image, so a resize never touches the map — the overlay just recomputes its
        /// scale from its own bounds. Presentation is suspended for the drag plus the host's 100ms
        /// debounce, so the overlay dims over the stale frame rather than being offset to match it.
        /// </summary>
        private void OnOverlaySizeChanged(object sender, EventArgs e)
        {
            _dragging = false;
            _overlayDrawable.Dimmed = true;
            _resizeSettleTimer.Stop();
            _resizeSettleTimer.Start();
            MapOverlay.Invalidate();
        }

        private void OnResizeSettled(object sender, EventArgs e)
        {
            _resizeSettleTimer.Stop();
            _overlayDrawable.Dimmed = false;
            MapOverlay.Invalidate();
        }

        // ---------------------------------------------------------------- fixtures

        private void SyncListSelection()
        {
            FixtureList.SelectedItem = _selectedFixtureId.HasValue
                ? _fixtureRows.FirstOrDefault(r => r.Id == _selectedFixtureId.Value)
                : null;
        }

        private void OnFixtureSelected(object sender, SelectionChangedEventArgs e)
        {
            _selectedFixtureId = (e.CurrentSelection.FirstOrDefault() as FixtureRow)?.Id;
            _overlayDrawable.SelectedFixtureId = _selectedFixtureId;
            MapOverlay.Invalidate();
        }

        private void OnToggleEnabledClicked(object sender, EventArgs e)
        {
            var fixture = SelectedFixture();
            if (fixture == null)
            {
                StatusLabel.Text = "Select a fixture first.";
                return;
            }

            fixture.Enabled = !fixture.Enabled;
            if (!PublishWorkingMap())
            {
                fixture.Enabled = !fixture.Enabled;
            }
        }

        private void OnRemoveFixtureClicked(object sender, EventArgs e)
        {
            var fixture = SelectedFixture();
            if (fixture == null)
            {
                StatusLabel.Text = "Select a fixture first.";
                return;
            }

            _working.Fixtures.Remove(fixture);
            _selectedFixtureId = null;
            if (!PublishWorkingMap())
            {
                AdoptCurrentMap();
            }
        }

        private void OnAddFixtureClicked(object sender, EventArgs e)
        {
            if (!TryReadInt(NewPixelCount.Text, out int pixelCount) || pixelCount <= 0)
            {
                StatusLabel.Text = "Pixel count must be a positive whole number.";
                return;
            }
            if (!TryReadFloat(NewSpacing.Text, out float spacing) || spacing <= 0f)
            {
                StatusLabel.Text = "Spacing must be a positive number.";
                return;
            }
            if (!TryReadFloat(NewAngle.Text, out float angle))
            {
                StatusLabel.Text = "Angle must be a number.";
                return;
            }
            if (!TryReadInt(NewStartChannel.Text, out int startChannel) || startChannel < 0)
            {
                StatusLabel.Text = "Start channel must be zero or greater.";
                return;
            }
            if (!TryReadInt(NewPort.Text, out int port))
            {
                port = OutputTarget.DefaultDdpPort;
            }
            if (!TryReadInt(NewUniverse.Text, out int universe) || universe < 0 || universe > 255)
            {
                universe = 0;
            }

            var interfaceType = NewInterface.SelectedIndex == (int)OutputInterfaceType.ArtNet
                ? OutputInterfaceType.ArtNet
                : OutputInterfaceType.Ddp;

            var fixture = new FixtureDefinition
            {
                Name = string.IsNullOrWhiteSpace(NewName.Text) ? $"Fixture {_working.Fixtures.Count + 1}" : NewName.Text.Trim(),
                Output = new OutputTarget
                {
                    Interface = interfaceType,
                    IpAddress = (NewIpAddress.Text ?? string.Empty).Trim(),
                    Port = port,
                    Universe = (byte)universe,
                    StartChannel = startChannel
                },
                PixelType = NewPixelType.SelectedItem as string ?? "WS2812B",
                // Spacing and angle are consumed here and not stored: the map keeps the resulting
                // positions, so editor tooling can grow without a schema change.
                PixelOffsets = PixelLayoutTools.GenerateLine(pixelCount, spacing, angle),
                OriginX = _working.ImageWidth / 4f,
                OriginY = _working.ImageHeight / 2f
            };

            _working.Fixtures.Add(fixture);
            _selectedFixtureId = fixture.Id;

            if (PublishWorkingMap())
            {
                SyncListSelection();
                StatusLabel.Text = $"Added '{fixture.Name}'. Drag it into place.";
            }
            else
            {
                _working.Fixtures.Remove(fixture);
                _selectedFixtureId = null;
            }
        }

        private FixtureDefinition SelectedFixture() => _selectedFixtureId.HasValue
            ? _working.Fixtures.FirstOrDefault(f => f.Id == _selectedFixtureId.Value)
            : null;

        private static bool TryReadInt(string text, out int value) =>
            int.TryParse((text ?? string.Empty).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value);

        private static bool TryReadFloat(string text, out float value) =>
            float.TryParse((text ?? string.Empty).Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out value);

        // ---------------------------------------------------------------- files

        private async void OnOpenClicked(object sender, EventArgs e)
        {
            try
            {
                if (_pixelMapEditor.IsDirty &&
                    !await DisplayAlert("Unsaved changes", "Opening another map discards the current unsaved edits.", "Open anyway", "Cancel"))
                {
                    return;
                }

                string path = await _fileDialog.PickOpenPathAsync();
                if (string.IsNullOrEmpty(path))
                {
                    return;
                }

                _pixelMapEditor.Open(path);
                AdoptCurrentMap();
                StatusLabel.Text = $"Opened {path}";
            }
            catch (Exception ex)
            {
                StatusLabel.Text = ex.Message;
                Serilog.Log.Error(ex, "[PreviewPage] Could not open a pixel map.");
            }
        }

        private async void OnSaveClicked(object sender, EventArgs e)
        {
            if (string.IsNullOrEmpty(_pixelMapEditor.SavedPath))
            {
                await SaveAs();
                return;
            }

            try
            {
                _pixelMapEditor.Save(_pixelMapEditor.SavedPath);
                RefreshEditorUi();
                StatusLabel.Text = $"Saved {_pixelMapEditor.SavedPath}";
            }
            catch (Exception ex)
            {
                StatusLabel.Text = ex.Message;
                Serilog.Log.Error(ex, "[PreviewPage] Could not save the pixel map.");
            }
        }

        private async void OnSaveAsClicked(object sender, EventArgs e) => await SaveAs();

        private async System.Threading.Tasks.Task SaveAs()
        {
            try
            {
                string suggested = System.IO.Path.GetFileName(_pixelMapEditor.SavedPath) ?? "pixelmap.json";
                string contents = PixelMapSerializer.Serialize(_pixelMapEditor.Current);
                string path = await _fileDialog.SaveAsAsync(suggested, contents);
                if (string.IsNullOrEmpty(path))
                {
                    return;
                }

                // Record the chosen path so plain Save goes back to it and the map is no longer dirty.
                _pixelMapEditor.Save(path);
                RefreshEditorUi();
                StatusLabel.Text = $"Saved {path}";
            }
            catch (Exception ex)
            {
                StatusLabel.Text = ex.Message;
                Serilog.Log.Error(ex, "[PreviewPage] Could not export the pixel map.");
            }
        }

        private async System.Threading.Tasks.Task PromptForRecoveredWork()
        {
            string action = await DisplayActionSheet(
                "A pixel map with unsaved changes was recovered from the last session.",
                cancel: null,
                destruction: "Discard",
                "Keep editing", "Save As…");

            if (action == "Discard")
            {
                _pixelMapEditor.DiscardRecoveredWork();
                AdoptCurrentMap();
            }
            else if (action == "Save As…")
            {
                _pixelMapEditor.KeepRecoveredWork();
                await SaveAs();
            }
            else
            {
                _pixelMapEditor.KeepRecoveredWork();
                RefreshEditorUi();
            }
        }

        // ---------------------------------------------------------------- surface

        private void UpdateShaderParamsLabel()
        {
            var shader = _graphicsHost.CurrentShader;
            string p1Label, p2Label, p3Label, p4Label;

            if (shader == Shaders.Kaleidoscope)
            {
                p1Label = "Spiral Twist";
                p2Label = "Time Offset";
                p3Label = "Ring Density";
                p4Label = "Flow Freq";
            }
            else if (shader == Shaders.Clown)
            {
                p1Label = "Blades";
                p2Label = "Spin Offset";
                p3Label = "Hue Offset";
                p4Label = "Dot Radius";
            }
            else
            {
                p1Label = "Param1";
                p2Label = "Param2";
                p3Label = "Param3";
                p4Label = "Param4";
            }

            ShaderParamsLabel.Text = $"Shader: {shader}\n" +
                $"Distortion: {_graphicsHost.DistortionStrength:F3}\n" +
                $"{p1Label}: {_graphicsHost.ShaderParam1:F3}\n" +
                $"{p2Label}: {_graphicsHost.ShaderParam2:F3}\n" +
                $"{p3Label}: {_graphicsHost.ShaderParam3:F3}\n" +
                $"{p4Label}: {_graphicsHost.ShaderParam4:F3}";
        }

        private void OnSurfaceReady(object sender, nint surfaceHandle)
        {
            System.Diagnostics.Debug.WriteLine("[PreviewPage] Surface ready");
#if MACCATALYST
            _graphicsHost.EnableMetalPresentation(surfaceHandle, (uint)vulkanSurface.Width, (uint)vulkanSurface.Height);
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
            System.Diagnostics.Debug.WriteLine($"[PreviewPage] Surface size changed: {e.Width}x{e.Height}");
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
            System.Diagnostics.Debug.WriteLine("[PreviewPage] Surface destroying");
        }

        protected override void OnDisappearing()
        {
            base.OnDisappearing();
            _paramTimer?.Stop();
            _resizeSettleTimer?.Stop();

            // Last chance on this window: the map must be on disk before it goes away.
            FlushWorkingCopy();
            _pixelMapEditor.Changed -= OnPixelMapChangedExternally;

            System.Diagnostics.Debug.WriteLine("[PreviewPage] Disappearing");
            _graphicsHost.OnPreviewWindowClosed();
        }

        /// <summary>Row shown in the fixture list. Rebuilt on every refresh, so it needs no change notification.</summary>
        private sealed class FixtureRow
        {
            public Guid Id { get; init; }

            public bool Enabled { get; init; }

            public string Description { get; init; }

            public static FixtureRow From(FixtureDefinition fixture) => new FixtureRow
            {
                Id = fixture.Id,
                Enabled = fixture.Enabled,
                Description = $"{fixture.Name} · {fixture.PixelCount}px · {fixture.Output.Key} ch {fixture.Output.StartChannel + 1}"
            };
        }
    }
}
