using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using ShowControl.PixelMapping;

namespace ShowControl.ControlPanel.Editor
{
    /// <summary>
    /// The pixel-map editor's state and operations, with no view attached.
    /// <para>
    /// Holds the working copy of the map, publishes edits through <see cref="IPixelMapEditor"/>, and
    /// owns the preview's zoom. The view keeps only what is genuinely platform: pointer coordinates,
    /// the transform on the video surface, the drawable, dialogs and timers.
    /// </para>
    /// </summary>
    public sealed partial class PixelMapEditorViewModel : EditorViewModelBase
    {
        private readonly IPixelMapEditor _editor;
        private readonly IPixelTypeResolver _pixelTypes;
        private readonly IPixelMapFileDialog? _fileDialog;
        private readonly IEditorDialogs? _dialogs;

        /// <summary>
        /// Set while publishing our own edit. Without it every pointer move would come back through
        /// <see cref="IPixelMapEditor.Changed"/>, re-clone the map and rebuild the row list at
        /// pointer rate.
        /// </summary>
        private bool _applyingOwnEdit;

        private Guid? _dragFixtureId;
        private double _dragStartImageX;
        private double _dragStartImageY;
        private float _dragStartOriginX;
        private float _dragStartOriginY;

        // A pixel drag: set when the press landed on the fixture that was already selected, so the
        // grab is one pixel rather than the whole run. The rest offsets are the shape as it stood
        // at the press; every move recomputes from them, which is what anchors the gesture the same
        // way the whole-fixture drag is anchored on its press point.
        private int? _dragPixelIndex;
        private float[]? _dragRestOffsets;

        // Panning: a drag on the preview where there was no fixture to grab. Anchored on the region
        // as it was at the press, because the transform moves underneath a pan and converting later
        // points through the live region would feed the pan its own output.
        private bool _panning;
        private PreviewRegion _panStartRegion;
        private double _panPressImageX;
        private double _panPressImageY;

        // Where the zoom was when edit mode was switched off, so leaving and re-entering does not
        // lose your place. The preview goes back to fit meanwhile: the zoom controls live in the
        // editor, and a magnified preview with no visible way to undo it is a trap mid-show.
        private double _stashedZoom = PreviewZoomViewModel.MinZoom;
        private double _stashedAnchorX = 0.5d;
        private double _stashedAnchorY = 0.5d;

        [ObservableProperty]
        private string _statusText = string.Empty;

        /// <summary>
        /// Whether the pixel-map editor is on. The overlay, the panel and the zoom controls are all
        /// bound to it, so turning it on and off is one property set rather than four assignments to
        /// controls — and the map/zoom bookkeeping that has to go with it happens in
        /// <see cref="OnIsEditingChanged"/> whatever flipped it.
        /// </summary>
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsNotEditing))]
        private bool _isEditing;

        [ObservableProperty]
        private string _dirtyText = string.Empty;

        [ObservableProperty]
        private string _spacingText = string.Empty;

        [ObservableProperty]
        private Guid? _selectedFixtureId;

        // Add-fixture form. Plain strings so the same validation runs whether the values came from
        // an entry box or a test.
        [ObservableProperty] private string _newName = string.Empty;
        [ObservableProperty] private string _newIpAddress = "192.168.3.207";
        [ObservableProperty] private string _newPort = "4048";
        [ObservableProperty] private string _newUniverse = "0";
        [ObservableProperty] private string _newStartChannel = "0";
        [ObservableProperty] private string _newPixelType = "WS2812B";
        [ObservableProperty] private string _newPixelCount = "720";
        [ObservableProperty] private string _newSpacing = "1.25";
        [ObservableProperty] private string _newAngle = "0";
        [ObservableProperty] private OutputInterfaceType _newInterface = OutputInterfaceType.Ddp;

        public PixelMapEditorViewModel(
            IPixelMapEditor editor,
            IPixelTypeResolver pixelTypes,
            ILogger<PixelMapEditorViewModel>? logger = null,
            IPixelMapFileDialog? fileDialog = null,
            IEditorDialogs? dialogs = null)
            : base(logger)
        {
            _editor = editor ?? throw new ArgumentNullException(nameof(editor));
            _pixelTypes = pixelTypes ?? throw new ArgumentNullException(nameof(pixelTypes));
            _fileDialog = fileDialog;
            _dialogs = dialogs;
            Zoom = new PreviewZoomViewModel(logger);
            ShaderParameters = new ShaderParametersViewModel();
            Rows = new ObservableCollection<FixtureRow>();
            // Materialized once, and as a concrete list: the catalogue does not change, a picker
            // binding that re-ran the resolver on every read would rebuild its item list each time it
            // was queried, and a picker's item source has to be an IList.
            KnownPixelTypes = _pixelTypes.KnownPixelTypes.ToList();

            // One clickable row per choice, each carrying the guarded body that picks it. Built once
            // here: the catalogue is fixed, and rebuilding the rows would reset the selection.
            InterfaceOptionRows = InterfaceOptions
                .Select(option => new OptionRow(
                    option.ToString(),
                    () => Guard("Interface", () => NewInterface = option)))
                .ToList();
            PixelTypeOptionRows = KnownPixelTypes
                .Select(type => new OptionRow(
                    type,
                    () => Guard("Pixel type", () => NewPixelType = type)))
                .ToList();
            SyncInterfaceSelection();
            SyncPixelTypeSelection();

            Working = editor.Current.Clone();
            Zoom.SetImageSize(Working.ImageWidth, Working.ImageHeight);
            RefreshRows();
            RefreshDirtyText();
        }

        /// <summary>Where the preview is looking. Presentation only; the rig samples the whole image.</summary>
        public PreviewZoomViewModel Zoom { get; }

        /// <summary>
        /// The shader knob readout drawn over the preview. Not editor state, but the preview page
        /// binds to this view-model and the readout is on that page, so it hangs here rather than
        /// giving the label a binding context of its own.
        /// </summary>
        public ShaderParametersViewModel ShaderParameters { get; }

        /// <summary>
        /// The map being edited. Applied to the session, which clones before publishing, so this
        /// instance is never the one the render thread is reading.
        /// </summary>
        public PixelMap Working { get; private set; }

        public ObservableCollection<FixtureRow> Rows { get; }

        /// <summary>The pixel types a new fixture can be, in the order the panel lists them.</summary>
        public IReadOnlyList<string> KnownPixelTypes { get; }

        /// <summary>
        /// The output interfaces a new fixture can use, taken from the enum rather than listed here
        /// so a third interface cannot quietly go missing from the panel.
        /// </summary>
        public IReadOnlyList<OutputInterfaceType> InterfaceOptions { get; } =
            Enum.GetValues<OutputInterfaceType>();

        /// <summary>
        /// The interface choices as clickable rows. The panel draws these itself rather than using a
        /// <c>Picker</c>, whose Mac Catalyst presentation is a thumb-wheel in a popover.
        /// </summary>
        public IReadOnlyList<OptionRow> InterfaceOptionRows { get; }

        /// <summary>The pixel type choices as clickable rows. See <see cref="InterfaceOptionRows"/>.</summary>
        public IReadOnlyList<OptionRow> PixelTypeOptionRows { get; }

        /// <summary>
        /// The overlay is input-transparent when the editor is off, so a show is not one stray click
        /// away from moving a fixture. Bindings cannot negate, hence the property.
        /// </summary>
        public bool IsNotEditing => !IsEditing;

        public bool IsDragging => _dragFixtureId.HasValue;

        /// <summary>
        /// The pixel a drag is holding, for the overlay's highlight. Null between gestures and
        /// during a whole-fixture drag.
        /// </summary>
        public int? DraggedPixelIndex => _dragPixelIndex;

        /// <summary>True while a drag is moving the view rather than a fixture.</summary>
        public bool IsPanning => _panning;

        /// <summary>
        /// The region as it was when the pan started. The view converts its pointer positions
        /// through this rather than the live one, so the grabbed image point stays under the cursor
        /// however far the view has already moved.
        /// </summary>
        public PreviewRegion PanStartRegion => _panStartRegion;

        public string? SavedPath => _editor.SavedPath;

        public bool RecoveredUnsavedWork => _editor.RecoveredUnsavedWork;

        /// <summary>Raised when the map changed underneath us — an open, or a discard of recovered work.</summary>
        public event EventHandler? WorkingMapReplaced;

        /// <summary>
        /// Raised after every successful publish, including each move of a drag. The view uses it to
        /// pace the autosave timer, which is a view concern (it owns the dispatcher) rather than one
        /// the editor should know about.
        /// </summary>
        public event EventHandler? Published;

        /// <summary>
        /// Raised when the working copy has to be on disk now rather than at the next autosave: a
        /// drag has ended, or edit mode has been switched off. The view owns the timer — it owns the
        /// dispatcher — so this is the view-model asking it to fire early rather than reaching for
        /// one itself.
        /// </summary>
        public event EventHandler? FlushRequested;

        public FixtureDefinition? SelectedFixture => SelectedFixtureId.HasValue
            ? Working.Fixtures.FirstOrDefault(f => f.Id == SelectedFixtureId.Value)
            : null;

        // ------------------------------------------------------------------ lifecycle

        /// <summary>
        /// Entering the editor adopts whatever the rig is running and puts the view back where it
        /// was last time; leaving it drops any gesture in flight, returns the preview to fit, and
        /// gets the working copy onto disk. All of it used to be a run of assignments in the
        /// switch's Toggled handler, which meant it only happened when the switch was what changed.
        /// </summary>
        partial void OnIsEditingChanged(bool value)
        {
            if (value)
            {
                AdoptCurrentMap();
                Zoom.Zoom = _stashedZoom;
                Zoom.AnchorX = _stashedAnchorX;
                Zoom.AnchorY = _stashedAnchorY;
                return;
            }

            EndPan();
            EndDrag();

            _stashedZoom = Zoom.Zoom;
            _stashedAnchorX = Zoom.AnchorX;
            _stashedAnchorY = Zoom.AnchorY;
            Zoom.FitCommand.Execute(null);

            FlushRequested?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>Takes a fresh copy of whatever is live, dropping a selection that no longer exists.</summary>
        public void AdoptCurrentMap()
        {
            Working = _editor.Current.Clone();
            Zoom.SetImageSize(Working.ImageWidth, Working.ImageHeight);

            if (SelectedFixtureId.HasValue && Working.Fixtures.All(f => f.Id != SelectedFixtureId.Value))
            {
                SelectedFixtureId = null;
            }

            RefreshRows();
            RefreshDirtyText();
            WorkingMapReplaced?.Invoke(this, EventArgs.Empty);
        }

        public void HandleExternalChange()
        {
            // Our own edits come back through the same event; the flag is what tells them apart,
            // since the session clones before publishing and reference equality cannot.
            if (_applyingOwnEdit)
            {
                return;
            }

            AdoptCurrentMap();
        }

        public void SaveWorkingCopy() => Guard("Autosave", () => _editor.SaveWorkingCopy());

        public void Save(string path) => Guard("Save", () =>
        {
            _editor.Save(path);
            RefreshDirtyText();
            StatusText = $"Saved {path}";
        });

        public void Open(string path)
        {
            _editor.Open(path);
            AdoptCurrentMap();
            StatusText = $"Opened {path}";
        }

        public void KeepRecoveredWork()
        {
            _editor.KeepRecoveredWork();
            RefreshDirtyText();
        }

        public void DiscardRecoveredWork()
        {
            _editor.DiscardRecoveredWork();
            AdoptCurrentMap();
        }

        public string SerializeCurrent() => PixelMapSerializer.Serialize(_editor.Current);

        // ------------------------------------------------------------------ publishing

        /// <summary>
        /// Validates and publishes the working map. Publishing recompiles the sample map, so what
        /// the shader animation reads out of the rendered image follows on the next frame.
        /// </summary>
        public bool TryApplyWorking()
        {
            IReadOnlyList<string> problems;
            _applyingOwnEdit = true;
            try
            {
                problems = _editor.Apply(Working);
            }
            finally
            {
                _applyingOwnEdit = false;
            }

            if (problems.Count > 0)
            {
                StatusText = string.Join(Environment.NewLine, problems);
                return false;
            }

            if (!IsDragging)
            {
                // Mid-drag the status line is the drag's own; EndDrag has the last word on it.
                StatusText = string.Empty;
            }

            Published?.Invoke(this, EventArgs.Empty);
            return true;
        }

        /// <summary>Publishes and brings the whole panel back in step. Not for the drag path.</summary>
        public bool PublishWorking()
        {
            if (!TryApplyWorking())
            {
                return false;
            }

            RefreshRows();
            RefreshDirtyText();
            return true;
        }

        /// <summary>
        /// Reconciles the row list in place. Never clears and refills: that reaches
        /// <c>UICollectionView.insertItemsAtIndexPaths:</c> as a burst of updates, which UIKit
        /// answers by throwing, and on Mac Catalyst that aborts the process. An unchanged rig
        /// touches the platform list not at all, because <see cref="FixtureRow"/> compares by value.
        /// </summary>
        public void RefreshRows()
        {
            for (int i = 0; i < Working.Fixtures.Count; i++)
            {
                var row = FixtureRow.From(Working.Fixtures[i]);
                if (i >= Rows.Count)
                {
                    Rows.Add(row);
                }
                else if (!Rows[i].Equals(row))
                {
                    Rows[i] = row;
                }
            }

            while (Rows.Count > Working.Fixtures.Count)
            {
                Rows.RemoveAt(Rows.Count - 1);
            }

            RefreshSpacingText();
        }

        public void RefreshDirtyText() => DirtyText = _editor.IsDirty
            ? "Unsaved changes" + (_editor.SavedPath == null ? string.Empty : " · " + _editor.SavedPath)
            : _editor.SavedPath ?? "Built-in default map";

        // ------------------------------------------------------------------ selection

        public void SelectFixtureAt(double imageX, double imageY, double thresholdImageUnits)
        {
            var hit = FixtureHitTester.HitTest(Working, imageX, imageY, thresholdImageUnits);
            SelectedFixtureId = hit?.Id;
        }

        partial void OnSelectedFixtureIdChanged(Guid? value) => RefreshSpacingText();

        // The rows are the only place the current choice is shown, so they follow the value rather
        // than being what holds it — setting NewInterface or NewPixelType from anywhere moves the mark.
        partial void OnNewInterfaceChanged(OutputInterfaceType value) => SyncInterfaceSelection();

        partial void OnNewPixelTypeChanged(string value) => SyncPixelTypeSelection();

        private void SyncInterfaceSelection()
        {
            for (int i = 0; i < InterfaceOptionRows.Count; i++)
            {
                InterfaceOptionRows[i].IsSelected = InterfaceOptions[i] == NewInterface;
            }
        }

        /// <summary>Matched case-insensitively: the registry resolves names that way, and a saved
        /// map may carry either casing.</summary>
        private void SyncPixelTypeSelection()
        {
            for (int i = 0; i < PixelTypeOptionRows.Count; i++)
            {
                PixelTypeOptionRows[i].IsSelected =
                    string.Equals(KnownPixelTypes[i], NewPixelType, StringComparison.OrdinalIgnoreCase);
            }
        }

        /// <summary>
        /// Spacing is not stored — the map keeps positions — so the box shows what the offsets
        /// measure out to rather than something recalled.
        /// </summary>
        private void RefreshSpacingText()
        {
            var fixture = SelectedFixture;
            SpacingText = fixture == null
                ? string.Empty
                : FormatSpacing(PixelLayoutTools.MeasureSpacing(fixture.PixelOffsets));
        }

        // ------------------------------------------------------------------ dragging

        /// <summary>
        /// A press on the preview: grabs the fixture under it, or starts a pan if there is none.
        /// The view supplies image coordinates and gets back nothing it has to act on — which of the
        /// two gestures is running is <see cref="IsDragging"/> and <see cref="IsPanning"/>.
        /// </summary>
        public void BeginGesture(double imageX, double imageY, double thresholdImageUnits)
        {
            _panning = false;
            if (!TryBeginDrag(imageX, imageY, thresholdImageUnits))
            {
                BeginPan(imageX, imageY);
            }
        }

        /// <summary>Ends whichever gesture was running. True if a fixture drag was one of them.</summary>
        public bool EndGesture()
        {
            EndPan();
            return EndDrag();
        }

        /// <summary>
        /// Starts a fixture drag if there is one under the point. Anchored on the press, so the
        /// grabbed point stays under the cursor however many moves are coalesced or dropped.
        /// </summary>
        public bool TryBeginDrag(double imageX, double imageY, double thresholdImageUnits)
        {
            var previousSelection = SelectedFixtureId;
            var hit = FixtureHitTester.HitTest(Working, imageX, imageY, thresholdImageUnits);
            SelectedFixtureId = hit?.Id;
            _dragFixtureId = hit?.Id;
            _dragPixelIndex = null;
            _dragRestOffsets = null;

            if (hit == null)
            {
                return false;
            }

            _dragStartImageX = imageX;
            _dragStartImageY = imageY;
            _dragStartOriginX = hit.OriginX;
            _dragStartOriginY = hit.OriginY;

            // Scoped press: the first drag on a fixture selects it and moves the whole run; a press
            // on the fixture that is already selected grabs the pixel under the cursor instead.
            // Pressing empty space deselects, so a whole-fixture move is always one click away.
            if (previousSelection.HasValue && previousSelection.Value == hit.Id)
            {
                int? pixelIndex = FixtureHitTester.HitTestPixel(hit, imageX, imageY, thresholdImageUnits);
                if (pixelIndex.HasValue)
                {
                    _dragPixelIndex = pixelIndex;
                    _dragRestOffsets = (float[])hit.PixelOffsets.Clone();
                    StatusText = $"Dragging pixel {pixelIndex.Value} of '{hit.Name}'.";
                    return true;
                }
            }

            StatusText = $"Dragging '{hit.Name}'.";
            return true;
        }

        /// <summary>
        /// Moves the whole fixture and publishes, which is what makes the lights follow the drag.
        /// <para>
        /// Deliberately touches nothing the panel or the list is bound to: no <see cref="Rows"/>
        /// churn, no dirty text, no status. Those refresh in <see cref="EndDrag"/>. Rebuilding the
        /// list here would run the platform collection view at pointer rate, which is both wasteful
        /// and, on Mac Catalyst, a crash.
        /// </para>
        /// </summary>
        public void DragTo(double imageX, double imageY)
        {
            if (!_dragFixtureId.HasValue)
            {
                return;
            }

            var fixture = Working.Fixtures.FirstOrDefault(f => f.Id == _dragFixtureId.Value);
            if (fixture == null)
            {
                CancelDrag();
                return;
            }

            if (_dragPixelIndex.HasValue)
            {
                DragPixelTo(fixture, imageX, imageY);
                return;
            }

            // The origin moves; the per-pixel offsets are what give the fixture its shape and are
            // deliberately untouched.
            float originX = _dragStartOriginX + (float)(imageX - _dragStartImageX);
            float originY = _dragStartOriginY + (float)(imageY - _dragStartImageY);
            if (originX == fixture.OriginX && originY == fixture.OriginY)
            {
                return;
            }

            float previousX = fixture.OriginX;
            float previousY = fixture.OriginY;
            fixture.OriginX = originX;
            fixture.OriginY = originY;

            if (!TryApplyWorking())
            {
                fixture.OriginX = previousX;
                fixture.OriginY = previousY;
                CancelDrag();
            }
        }

        /// <summary>
        /// Moves one pixel and lets the run follow it as an inextensible rope — neighbours come
        /// along only when their link is pulled taut, slack absorbs everything else — recomputed on
        /// every move from the offsets stashed at the press. Publishes each move exactly as the
        /// whole-fixture drag does — that is what makes the lights follow the pull — and touches
        /// nothing the panel is bound to; <see cref="EndDrag"/> has the last word.
        /// </summary>
        private void DragPixelTo(FixtureDefinition fixture, double imageX, double imageY)
        {
            var rest = _dragRestOffsets;
            int pixelIndex = _dragPixelIndex!.Value;
            if (rest == null || rest.Length != fixture.PixelOffsets.Length || ((pixelIndex * 2) + 1) >= rest.Length)
            {
                // The fixture changed shape under the gesture (an external edit adopted mid-drag);
                // the stash no longer describes it, so the drag cannot continue safely.
                CancelDrag();
                return;
            }

            float deltaX = (float)(imageX - _dragStartImageX);
            float deltaY = (float)(imageY - _dragStartImageY);

            // The whole shape is a function of the dragged pixel's position, so an unmoved target
            // is an unmoved run — the same no-op skip the origin drag makes.
            var previous = fixture.PixelOffsets;
            float targetX = rest[pixelIndex * 2] + deltaX;
            float targetY = rest[(pixelIndex * 2) + 1] + deltaY;
            if (previous[pixelIndex * 2] == targetX && previous[(pixelIndex * 2) + 1] == targetY)
            {
                return;
            }

            var updated = (float[])rest.Clone();
            PixelLayoutTools.DragRope(updated, pixelIndex, deltaX, deltaY);
            fixture.PixelOffsets = updated;

            if (!TryApplyWorking())
            {
                fixture.PixelOffsets = previous;
                CancelDrag();
            }
        }

        private void CancelDrag()
        {
            _dragFixtureId = null;
            _dragPixelIndex = null;
            _dragRestOffsets = null;
        }

        /// <summary>Ends the drag and lets the panel catch up. Returns true if a drag was running.</summary>
        public bool EndDrag()
        {
            if (!_dragFixtureId.HasValue)
            {
                return false;
            }

            var dragged = Working.Fixtures.FirstOrDefault(f => f.Id == _dragFixtureId.Value);
            int? pixelIndex = _dragPixelIndex;
            bool wasDragStatus = StatusText.StartsWith("Dragging", StringComparison.Ordinal);
            CancelDrag();

            // A pixel drag can carry pixel 0 off the origin. Rebase so the origin marks pixel 0
            // again — no pixel moves, the same positions are just expressed by convention — and
            // republish so the session's copy is the rebased one the file will record.
            if (pixelIndex.HasValue && dragged != null && dragged.PixelOffsets.Length >= 2
                && (dragged.PixelOffsets[0] != 0f || dragged.PixelOffsets[1] != 0f))
            {
                var shift = PixelLayoutTools.Rebase(dragged.PixelOffsets);
                dragged.OriginX += shift.X;
                dragged.OriginY += shift.Y;
                TryApplyWorking();
            }

            RefreshRows();
            RefreshDirtyText();

            if (dragged != null && wasDragStatus)
            {
                if (pixelIndex.HasValue)
                {
                    var position = PixelMapGeometry.Resolve(dragged, pixelIndex.Value);
                    int outside = FixtureHitTester.CountPixelsOutsideImage(dragged, Working.ImageWidth, Working.ImageHeight);
                    string outsideNote = outside == 0
                        ? string.Empty
                        : $" {outside} of {dragged.PixelCount} pixels fall outside the {Working.ImageWidth}x{Working.ImageHeight} image and stay dark.";
                    StatusText = $"'{dragged.Name}' pixel {pixelIndex.Value} moved to {position.X:F0}, {position.Y:F0}.{outsideNote}";
                }
                else
                {
                    StatusText = $"'{dragged.Name}' origin {dragged.OriginX:F0}, {dragged.OriginY:F0}.";
                }
            }

            // A drag publishes on every move, so by the time it ends the working copy is well ahead
            // of the file. Ask for it now rather than waiting out the autosave interval.
            FlushRequested?.Invoke(this, EventArgs.Empty);
            return true;
        }

        // ------------------------------------------------------------------ panning

        /// <summary>
        /// Starts moving the view. A pan is not an edit: nothing is published, nothing is autosaved,
        /// the map is not touched.
        /// </summary>
        public void BeginPan(double imageX, double imageY)
        {
            _panning = true;
            _panStartRegion = Zoom.Region;
            _panPressImageX = imageX;
            _panPressImageY = imageY;
        }

        /// <summary>
        /// Moves the view so the grabbed image point is back under the cursor. The coordinates come
        /// from the view converted through <see cref="PanStartRegion"/>, not the live region.
        /// </summary>
        public void PanTo(double imageX, double imageY)
        {
            if (!_panning)
            {
                return;
            }

            Zoom.MoveTo(
                _panStartRegion.X + (_panPressImageX - imageX),
                _panStartRegion.Y + (_panPressImageY - imageY));
        }

        public void EndPan() => _panning = false;

        // ------------------------------------------------------------------ commands

        [RelayCommand]
        private void ToggleEnabled() => Guard("Toggle enabled", () =>
        {
            var fixture = SelectedFixture;
            if (fixture == null)
            {
                StatusText = "Select a fixture first.";
                return;
            }

            fixture.Enabled = !fixture.Enabled;
            if (!PublishWorking())
            {
                fixture.Enabled = !fixture.Enabled;
            }
        });

        [RelayCommand]
        private void RemoveFixture() => Guard("Remove fixture", () =>
        {
            var fixture = SelectedFixture;
            if (fixture == null)
            {
                StatusText = "Select a fixture first.";
                return;
            }

            Working.Fixtures.Remove(fixture);
            SelectedFixtureId = null;
            if (!PublishWorking())
            {
                AdoptCurrentMap();
            }
        });

        /// <summary>
        /// Drives the fixture from its other end. The run does not move — the pixel order reverses —
        /// so like respacing this is a geometry change the lights pick up immediately.
        /// </summary>
        [RelayCommand]
        private void FlipDirection() => Guard("Flip direction", () =>
        {
            var fixture = SelectedFixture;
            if (fixture == null)
            {
                StatusText = "Select a fixture first.";
                return;
            }

            if (fixture.PixelCount < 2)
            {
                StatusText = $"'{fixture.Name}' is a single pixel; it has no direction to flip.";
                return;
            }

            var previousOffsets = fixture.PixelOffsets;
            float previousOriginX = fixture.OriginX;
            float previousOriginY = fixture.OriginY;

            var flipped = (float[])previousOffsets.Clone();
            var shift = PixelLayoutTools.ReverseDirection(flipped);
            fixture.PixelOffsets = flipped;
            fixture.OriginX += shift.X;
            fixture.OriginY += shift.Y;

            if (!PublishWorking())
            {
                fixture.PixelOffsets = previousOffsets;
                fixture.OriginX = previousOriginX;
                fixture.OriginY = previousOriginY;
                return;
            }

            // Blanked pixels are wire positions, so a dead tail stays the tail of the wire and is
            // now drawn at the other end of the run. Worth saying — it looks like a bug otherwise.
            string blanked = fixture.BlankedPixels.Count > 0
                ? " Its blanked pixels keep their wire positions, so they move to the other end."
                : string.Empty;
            StatusText = $"'{fixture.Name}' now runs from the opposite end.{blanked}";
        });

        [RelayCommand]
        private void ApplySpacing() => Guard("Pixel spacing", () =>
        {
            var fixture = SelectedFixture;
            if (fixture == null)
            {
                StatusText = "Select a fixture first.";
                return;
            }

            if (!TryReadFloat(SpacingText, out float spacing) || spacing <= 0f || !float.IsFinite(spacing))
            {
                StatusText = "Spacing must be a positive number.";
                return;
            }

            if (PixelLayoutTools.MeasureSpacing(fixture.PixelOffsets) <= 0f)
            {
                StatusText = $"'{fixture.Name}' has no spacing to change — it is a single pixel.";
                return;
            }

            var previous = fixture.PixelOffsets;
            var updated = (float[])previous.Clone();
            PixelLayoutTools.SetSpacing(updated, spacing);
            fixture.PixelOffsets = updated;

            if (!PublishWorking())
            {
                fixture.PixelOffsets = previous;
                return;
            }

            int outside = FixtureHitTester.CountPixelsOutsideImage(fixture, Working.ImageWidth, Working.ImageHeight);
            StatusText = outside == 0
                ? $"'{fixture.Name}' spacing {FormatSpacing(spacing)}."
                : $"'{fixture.Name}' spacing {FormatSpacing(spacing)} — {outside} of {fixture.PixelCount} pixels now fall outside the {Working.ImageWidth}x{Working.ImageHeight} image and stay dark.";
        });

        [RelayCommand]
        private void AddFixture() => Guard("Add fixture", () =>
        {
            if (!TryReadInt(NewPixelCount, out int pixelCount) || pixelCount <= 0)
            {
                StatusText = "Pixel count must be a positive whole number.";
                return;
            }
            if (!TryReadFloat(NewSpacing, out float spacing) || spacing <= 0f)
            {
                StatusText = "Spacing must be a positive number.";
                return;
            }
            if (!TryReadFloat(NewAngle, out float angle))
            {
                StatusText = "Angle must be a number.";
                return;
            }
            if (!TryReadInt(NewStartChannel, out int startChannel) || startChannel < 0)
            {
                StatusText = "Start channel must be zero or greater.";
                return;
            }
            if (!TryReadInt(NewPort, out int port))
            {
                port = OutputTarget.DefaultDdpPort;
            }
            if (!TryReadInt(NewUniverse, out int universe) || universe < 0 || universe > 255)
            {
                universe = 0;
            }

            var fixture = new FixtureDefinition
            {
                Name = string.IsNullOrWhiteSpace(NewName) ? $"Fixture {Working.Fixtures.Count + 1}" : NewName.Trim(),
                Output = new OutputTarget
                {
                    Interface = NewInterface,
                    IpAddress = (NewIpAddress ?? string.Empty).Trim(),
                    Port = port,
                    Universe = (byte)universe,
                    StartChannel = startChannel
                },
                PixelType = string.IsNullOrWhiteSpace(NewPixelType) ? "WS2812B" : NewPixelType,
                // Spacing and angle are consumed here and not stored: the map keeps the resulting
                // positions, so editor tooling can grow without a schema change.
                PixelOffsets = PixelLayoutTools.GenerateLine(pixelCount, spacing, angle),
                OriginX = Working.ImageWidth / 4f,
                OriginY = Working.ImageHeight / 2f
            };

            Working.Fixtures.Add(fixture);
            SelectedFixtureId = fixture.Id;

            if (PublishWorking())
            {
                StatusText = $"Added '{fixture.Name}'. Drag it into place.";
            }
            else
            {
                Working.Fixtures.Remove(fixture);
                SelectedFixtureId = null;
            }
        });

        // ------------------------------------------------------------------ file commands

        /// <summary>
        /// Opens another map, confirming first if the current one has unsaved edits. The dialogs are
        /// injected, so the decision — ask, then honour the answer — is testable and only the
        /// platform's file chooser stays behind in the view.
        /// </summary>
        [RelayCommand]
        private Task OpenAsync() => GuardAsync("Open", async () =>
        {
            if (!HaveDialogs())
            {
                return;
            }

            if (_editor.IsDirty && !await _dialogs!.ConfirmDiscardingUnsavedChangesAsync())
            {
                return;
            }

            string? path = await _fileDialog!.PickOpenPathAsync();
            if (string.IsNullOrEmpty(path))
            {
                return;
            }

            Open(path!);
        });

        /// <summary>Saves back where it came from, or asks where to put it the first time.</summary>
        [RelayCommand]
        private Task SaveAsync() => GuardAsync("Save", async () =>
        {
            if (string.IsNullOrEmpty(SavedPath))
            {
                await SaveAsCoreAsync();
                return;
            }

            Save(SavedPath!);
        });

        [RelayCommand]
        private Task SaveAsAsync() => GuardAsync("Save As", SaveAsCoreAsync);

        /// <summary>
        /// The prompt the operator did not get on the way out: Mac Catalyst has no cancellable
        /// terminate hook under the sandbox, so unsaved edits are flushed to the working copy and
        /// asked about here instead. The view calls this once its window is up.
        /// </summary>
        [RelayCommand]
        private Task ReviewRecoveredWorkAsync() => GuardAsync("Recovered work", async () =>
        {
            if (!RecoveredUnsavedWork || !HaveDialogs())
            {
                return;
            }

            var choice = await _dialogs!.AskAboutRecoveredWorkAsync();
            if (choice == RecoveredWorkChoice.Discard)
            {
                DiscardRecoveredWork();
                return;
            }

            KeepRecoveredWork();
            if (choice == RecoveredWorkChoice.SaveAs)
            {
                await SaveAsCoreAsync();
            }
        });

        private async Task SaveAsCoreAsync()
        {
            if (!HaveDialogs())
            {
                return;
            }

            string suggested = string.IsNullOrEmpty(SavedPath)
                ? "pixelmap.json"
                : Path.GetFileName(SavedPath!);

            string? path = await _fileDialog!.SaveAsAsync(suggested, SerializeCurrent());
            if (string.IsNullOrEmpty(path))
            {
                return;
            }

            // Record the chosen path so plain Save goes back to it and the map is no longer dirty.
            Save(path!);
        }

        /// <summary>
        /// A view-model built without dialogs — a test, or a host that has none — can still do
        /// everything except ask a question. Saying so on the status line beats a null reference out
        /// of a command body.
        /// </summary>
        private bool HaveDialogs()
        {
            if (_fileDialog != null && _dialogs != null)
            {
                return true;
            }

            StatusText = "No file dialogs are available in this host.";
            return false;
        }

        protected override void OnGuardedFailure(string what, Exception exception) =>
            StatusText = $"{what} failed: {exception.Message}";

        private static string FormatSpacing(float spacing) =>
            spacing.ToString("0.###", CultureInfo.InvariantCulture);

        private static bool TryReadInt(string text, out int value) =>
            int.TryParse((text ?? string.Empty).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value);

        private static bool TryReadFloat(string text, out float value) =>
            float.TryParse((text ?? string.Empty).Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }
}
