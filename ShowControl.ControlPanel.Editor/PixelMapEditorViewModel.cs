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
        // grab is one pixel rather than the whole run. The grabbed pixel is the handle of a hinge
        // at the pixel before it — the rest of the run swings to point at the cursor — and the rest
        // offsets are the shape as it stood at the press; every move recomputes from them, which is
        // what anchors the gesture the same way the whole-fixture drag is anchored on its press
        // point. The heading is the last one the gesture set, for the status line.
        private int? _dragPixelIndex;
        private float[]? _dragRestOffsets;
        private float? _dragHeading;

        /// <summary>
        /// How close to a multiple of 45° a bend has to land to snap to it. Wide enough to hit a
        /// right angle by eye at any zoom, narrow enough that a deliberate 40° is still 40°.
        /// </summary>
        private const float BendSnapToleranceDegrees = 4f;

        // The start channel the form last suggested. While the box still holds it the operator has
        // not typed their own, so a change of controller or a newly added strip moves the box too;
        // once it differs the box is theirs and only the hint beside it follows.
        private string _lastSuggestedStartChannel = "0";

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

        /// <summary>Total turn of the selected run in degrees, measured off its offsets; zero when straight.</summary>
        [ObservableProperty]
        private string _bendText = string.Empty;

        /// <summary>
        /// The selected fixture's blanked pixels in the editor's notation (<c>12-15, 40</c>, zero-based,
        /// inclusive). Read back from the fixture on selection; applied through <see cref="ApplyBlankedCommand"/>.
        /// </summary>
        [ObservableProperty]
        private string _blankedText = string.Empty;

        /// <summary>
        /// Every field of the selected fixture, one per line, including the channel range it
        /// occupies. The list row is a single truncating line; this is where the whole record is
        /// readable, which is what wiring the next strip needs.
        /// </summary>
        [ObservableProperty]
        private string _selectedFixtureDetail = string.Empty;

        /// <summary>Where the suggested start channel came from, shown beside the box.</summary>
        [ObservableProperty]
        private string _startChannelHint = string.Empty;

        [ObservableProperty]
        private Guid? _selectedFixtureId;

        /// <summary>What the rig is showing instead of the animation, for the panel's readout.</summary>
        [ObservableProperty]
        private string _outputOverrideText = string.Empty;

        // Fixture form: what Add builds a fixture from, and what Apply-to-selected writes onto one
        // (Load-selected fills it from the selection). Plain strings so the same validation runs
        // whether the values came from an entry box or a test.
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
            RefreshOutputOverrideText();
        }

        /// <summary>The wiring-check override the rig is showing: a test pattern, a soloed fixture, or nothing.</summary>
        public PixelOutputOverrideRequest OutputOverride => _editor.CurrentOutputOverride;

        /// <summary>
        /// The pixel-type catalogue the editor resolves channel counts through, for the readouts
        /// that need channels per pixel.
        /// </summary>
        public IPixelTypeResolver PixelTypes => _pixelTypes;

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

            // A test pattern or a solo left on after the editor closes would be a show with one
            // strip lit, and nothing on screen to say why. Leaving edit mode is the off switch.
            SetOutputOverride(PixelOutputOverrideRequest.Nothing);

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
                var row = FixtureRow.From(Working.Fixtures[i], _pixelTypes);
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

            RefreshSelectionReadout();
            RefreshStartChannelSuggestion();
            RefreshOutputOverrideText();
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

        partial void OnSelectedFixtureIdChanged(Guid? value) => RefreshSelectionReadout();

        // The rows are the only place the current choice is shown, so they follow the value rather
        // than being what holds it — setting NewInterface or NewPixelType from anywhere moves the mark.
        partial void OnNewInterfaceChanged(OutputInterfaceType value)
        {
            SyncInterfaceSelection();
            RefreshStartChannelSuggestion();
        }

        // The suggested start channel is a property of the target, so it follows every field that
        // identifies one.
        partial void OnNewIpAddressChanged(string value) => RefreshStartChannelSuggestion();

        partial void OnNewPortChanged(string value) => RefreshStartChannelSuggestion();

        partial void OnNewUniverseChanged(string value) => RefreshStartChannelSuggestion();

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
        /// Spacing and bend are not stored — the map keeps positions — so the boxes show what the
        /// offsets measure out to rather than something recalled. The detail block is rebuilt with
        /// them: it is derived from the same fixture and goes stale at the same moments.
        /// </summary>
        private void RefreshSelectionReadout()
        {
            var fixture = SelectedFixture;
            if (fixture == null)
            {
                SpacingText = string.Empty;
                BendText = string.Empty;
                BlankedText = string.Empty;
                SelectedFixtureDetail = "Nothing selected. Click a fixture in the list or on the preview.";
                return;
            }

            SpacingText = FormatSpacing(PixelLayoutTools.MeasureSpacing(fixture.PixelOffsets));
            BendText = FormatDegrees(PixelLayoutTools.MeasureBend(fixture.PixelOffsets));
            BlankedText = PixelRange.Format(fixture.BlankedPixels);
            SelectedFixtureDetail = DescribeFixture(fixture);
        }

        /// <summary>
        /// The whole record, one field per line, in the order the add form asks for them so the two
        /// read against each other. Channels are the zero-based offsets the file stores: the end
        /// channel here is the number the next strip on the same controller starts after.
        /// </summary>
        private string DescribeFixture(FixtureDefinition fixture)
        {
            var output = fixture.Output;
            bool knownType = _pixelTypes.TryGetChannelCount(fixture.PixelType, out int channelsPerPixel);
            int channelCount = knownType ? fixture.PixelCount * channelsPerPixel : 0;
            int outside = FixtureHitTester.CountPixelsOutsideImage(fixture, Working.ImageWidth, Working.ImageHeight);

            var lines = new List<string>
            {
                $"{fixture.Name}" + (fixture.Enabled ? string.Empty : "  (disabled)"),
                output.Interface == OutputInterfaceType.ArtNet
                    ? $"ArtNet {output.IpAddress} · universe {output.Universe}"
                    : $"DDP {output.IpAddress} · port {output.Port}",
                knownType
                    ? $"{fixture.PixelType} · {channelsPerPixel} ch/px · {fixture.PixelCount} px · {channelCount} ch"
                    : $"{fixture.PixelType} (unknown type) · {fixture.PixelCount} px",
                knownType && channelCount > 0
                    ? $"Channels {output.StartChannel}–{output.StartChannel + channelCount - 1} (next free {output.StartChannel + channelCount})"
                    : $"Start channel {output.StartChannel}",
                $"Origin {fixture.OriginX:F1}, {fixture.OriginY:F1} · spacing {SpacingText} · bend {BendText}°",
                fixture.BlankedPixels.Count == 0
                    ? "No blanked pixels"
                    : $"Blanked {PixelRange.Format(fixture.BlankedPixels)} ({fixture.BlankedPixels.Sum(r => r.Count)} px dark)"
            };

            if (outside > 0)
            {
                lines.Add($"{outside} of {fixture.PixelCount} px outside the {Working.ImageWidth}x{Working.ImageHeight} image (dark)");
            }

            return string.Join(Environment.NewLine, lines);
        }

        /// <summary>
        /// Recomputes the start channel the add form suggests: the first free channel on the target
        /// the form currently describes. The box follows the suggestion until the operator types
        /// something else, and the hint says which strip the number comes after so it can be checked
        /// against the list.
        /// </summary>
        private void RefreshStartChannelSuggestion()
        {
            var key = NewTargetKey();
            int next = ChannelPlanner.NextFreeChannel(Working, key, _pixelTypes, out var after);
            string suggested = next.ToString(CultureInfo.InvariantCulture);

            StartChannelHint = after == null
                ? $"Nothing on {key} yet, so 0."
                : $"Next free on {key} is {suggested}, after '{after.Name}'.";

            bool following = string.Equals(NewStartChannel?.Trim(), _lastSuggestedStartChannel, StringComparison.Ordinal);
            _lastSuggestedStartChannel = suggested;
            if (following)
            {
                NewStartChannel = suggested;
            }
        }

        /// <summary>The output target the add form currently describes, with the same fallbacks AddFixture uses.</summary>
        private OutputTargetKey NewTargetKey()
        {
            if (!TryReadInt(NewPort, out int port))
            {
                port = OutputTarget.DefaultDdpPort;
            }
            if (!TryReadInt(NewUniverse, out int universe) || universe < 0 || universe > 255)
            {
                universe = 0;
            }

            return new OutputTargetKey(NewInterface, (NewIpAddress ?? string.Empty).Trim(), port, (byte)universe);
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
            _dragHeading = null;

            // Scoped press: the first drag on a fixture selects it and moves the whole run; a press
            // on the fixture that is already selected grabs the pixel under the cursor instead, as
            // the handle of a hinge at the pixel before it. Pressing empty space deselects, so a
            // whole-fixture move is always one click away.
            if (previousSelection.HasValue && previousSelection.Value == hit.Id)
            {
                int? pixelIndex = FixtureHitTester.HitTestPixel(hit, imageX, imageY, thresholdImageUnits);
                if (pixelIndex.HasValue)
                {
                    _dragPixelIndex = pixelIndex;
                    _dragRestOffsets = (float[])hit.PixelOffsets.Clone();
                    StatusText = pixelIndex.Value == 0
                        ? $"Dragging pixel 0 of '{hit.Name}': rotating the run about its first pixel."
                        : $"Dragging pixel {pixelIndex.Value} of '{hit.Name}': bending at pixel {pixelIndex.Value - 1}, the rest of the run follows the cursor. Snaps to 45°.";
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
        /// Bends the run: the grabbed pixel and everything after it swing about the pixel before it
        /// to point at the cursor, recomputed on every move from the offsets stashed at the press.
        /// Pixel 0 rotates the whole run about the origin instead. Publishes each move exactly as the
        /// whole-fixture drag does — that is what makes the lights follow the bend — and touches
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

            // The target is the cursor in offset space: the hinge is a stored offset, and the
            // heading is measured from it.
            float targetX = (float)(imageX - fixture.OriginX);
            float targetY = (float)(imageY - fixture.OriginY);

            var updated = (float[])rest.Clone();
            float? heading = PixelLayoutTools.PointSectionAt(updated, pixelIndex, targetX, targetY, BendSnapToleranceDegrees);
            if (!heading.HasValue)
            {
                // Too close to the hinge to have a direction, or a single pixel: nothing to do.
                return;
            }

            // The whole shape is a function of the heading, so an unchanged heading is an unchanged
            // run — the same no-op skip the origin drag makes.
            var previous = fixture.PixelOffsets;
            if (heading == _dragHeading && previous.AsSpan().SequenceEqual(updated))
            {
                return;
            }

            _dragHeading = heading;
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
            _dragHeading = null;
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
            float? heading = _dragHeading;
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
                    int outside = FixtureHitTester.CountPixelsOutsideImage(dragged, Working.ImageWidth, Working.ImageHeight);
                    string outsideNote = outside == 0
                        ? string.Empty
                        : $" {outside} of {dragged.PixelCount} pixels fall outside the {Working.ImageWidth}x{Working.ImageHeight} image and stay dark.";
                    string what = !heading.HasValue
                        ? $"'{dragged.Name}' pixel {pixelIndex.Value} was not moved."
                        : pixelIndex.Value == 0
                            ? $"'{dragged.Name}' rotated to {FormatDegrees(heading.Value)}°."
                            : $"'{dragged.Name}' bent at pixel {pixelIndex.Value - 1}: the run from pixel {pixelIndex.Value} now heads {FormatDegrees(heading.Value)}°, {FormatDegrees(PixelLayoutTools.MeasureBend(dragged.PixelOffsets))}° of bend in all. Straighten undoes it.";
                    StatusText = what + outsideNote;
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

        // ------------------------------------------------------------------ wiring checks

        private void SetOutputOverride(PixelOutputOverrideRequest request)
        {
            _editor.SetOutputOverride(request);
            RefreshOutputOverrideText();
        }

        private void RefreshOutputOverrideText()
        {
            var request = _editor.CurrentOutputOverride;
            string solo = request.SoloFixtureId is Guid id
                ? Working.Fixtures.FirstOrDefault(f => f.Id == id)?.Name is string name
                    ? $"Solo '{name}'"
                    : "Solo (fixture gone — everything dark)"
                : null;
            string pattern = request.Pattern == PixelTestPattern.None ? null : $"Pattern: {DescribePattern(request.Pattern)}";
            OutputOverrideText = solo == null && pattern == null
                ? "Showing the animation."
                : string.Join(" · ", new[] { pattern, solo }.Where(s => s != null)) + " — the rig is not showing the animation.";
        }

        private static string DescribePattern(PixelTestPattern pattern) => pattern switch
        {
            PixelTestPattern.Chase => "chase (direction, length, dead pixels)",
            PixelTestPattern.Rainbow => "rainbow (order along each strip)",
            PixelTestPattern.FixtureColors => "a colour per fixture",
            PixelTestPattern.White => "white",
            PixelTestPattern.Diagonals => "diagonals (lines across strips; a break is a strip out of place)",
            _ => "none"
        };

        /// <summary>Puts a test pattern on the rig, or takes it off with <see cref="PixelTestPattern.None"/>. Any solo stays.</summary>
        [RelayCommand]
        private void SetTestPattern(PixelTestPattern pattern) => Guard("Test pattern", () =>
            SetOutputOverride(_editor.CurrentOutputOverride with { Pattern = pattern }));

        /// <summary>Solos the selected fixture — every other strip goes dark — or, pressed again on the same one, un-solos it.</summary>
        [RelayCommand]
        private void SoloSelected() => Guard("Solo", () =>
        {
            var fixture = SelectedFixture;
            if (fixture == null)
            {
                StatusText = "Select a fixture first.";
                return;
            }
            var current = _editor.CurrentOutputOverride;
            bool unsolo = current.SoloFixtureId == fixture.Id;
            SetOutputOverride(current with { SoloFixtureId = unsolo ? null : fixture.Id });
            StatusText = unsolo
                ? $"'{fixture.Name}' is no longer soloed."
                : $"Soloed '{fixture.Name}': every other strip is dark until you un-solo or leave edit mode.";
        });

        /// <summary>Everything back to the animation.</summary>
        [RelayCommand]
        private void ClearOutputOverride() => Guard("Clear wiring check", () =>
        {
            SetOutputOverride(PixelOutputOverrideRequest.Nothing);
            StatusText = "The rig is showing the animation again.";
        });

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

        /// <summary>
        /// The way back from any bend: a straight, evenly spaced run from the first pixel toward
        /// where the last one was. Pixel 0 stays put, so the fixture does not move.
        /// </summary>
        [RelayCommand]
        private void Straighten() => Guard("Straighten", () =>
        {
            var fixture = SelectedFixture;
            if (fixture == null)
            {
                StatusText = "Select a fixture first.";
                return;
            }

            if (fixture.PixelCount < 2)
            {
                StatusText = $"'{fixture.Name}' is a single pixel; there is nothing to straighten.";
                return;
            }

            var previous = fixture.PixelOffsets;
            var updated = (float[])previous.Clone();
            PixelLayoutTools.Straighten(updated);
            fixture.PixelOffsets = updated;

            if (!PublishWorking())
            {
                fixture.PixelOffsets = previous;
                return;
            }

            StatusText = $"'{fixture.Name}' is straight again, heading {FormatDegrees(HeadingOf(fixture))}°.";
        });

        /// <summary>
        /// Lays the selected run out as an even arc with the typed total turn: 0 is straight, 90 a
        /// quarter turn, 360 a ring. The first link keeps its heading, so the arc starts off in the
        /// direction the strip already leaves its first pixel.
        /// </summary>
        [RelayCommand]
        private void ApplyBend() => Guard("Bend", () =>
        {
            var fixture = SelectedFixture;
            if (fixture == null)
            {
                StatusText = "Select a fixture first.";
                return;
            }

            if (!TryReadFloat(BendText, out float degrees) || !float.IsFinite(degrees))
            {
                StatusText = "Bend must be a number of degrees.";
                return;
            }

            if (fixture.PixelCount < 3)
            {
                StatusText = $"'{fixture.Name}' has fewer than three pixels; it cannot bend.";
                return;
            }

            var previous = fixture.PixelOffsets;
            var updated = (float[])previous.Clone();
            PixelLayoutTools.SetBend(updated, degrees);
            fixture.PixelOffsets = updated;

            if (!PublishWorking())
            {
                fixture.PixelOffsets = previous;
                return;
            }

            int outside = FixtureHitTester.CountPixelsOutsideImage(fixture, Working.ImageWidth, Working.ImageHeight);
            StatusText = outside == 0
                ? $"'{fixture.Name}' bent {FormatDegrees(degrees)}° in all."
                : $"'{fixture.Name}' bent {FormatDegrees(degrees)}° in all — {outside} of {fixture.PixelCount} pixels now fall outside the {Working.ImageWidth}x{Working.ImageHeight} image and stay dark.";
        });

        /// <summary>Puts the suggested start channel in the box, for an operator who typed over it.</summary>
        [RelayCommand]
        private void UseNextFreeChannel() => Guard("Start channel", () =>
        {
            RefreshStartChannelSuggestion();
            NewStartChannel = _lastSuggestedStartChannel;
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

        /// <summary>
        /// Blanks the pixels typed in the box — a dead tail, a gap where a segment was cut out —
        /// and un-blanks the rest. They still occupy their channels, since addressing is positional
        /// on the wire; they sample as black and the overlay draws them hollow. A change to the
        /// sample map, so the lights pick it up at once.
        /// </summary>
        [RelayCommand]
        private void ApplyBlanked() => Guard("Blanked pixels", () =>
        {
            var fixture = SelectedFixture;
            if (fixture == null)
            {
                StatusText = "Select a fixture first.";
                return;
            }

            if (!PixelRange.TryParseList(BlankedText, fixture.PixelCount, out var ranges, out string? error))
            {
                StatusText = error!;
                return;
            }

            var previous = fixture.BlankedPixels;
            fixture.BlankedPixels = ranges;

            if (!PublishWorking())
            {
                fixture.BlankedPixels = previous;
                return;
            }

            int dark = ranges.Sum(r => r.Count);
            StatusText = dark == 0
                ? $"'{fixture.Name}' has no blanked pixels."
                : $"'{fixture.Name}' blanks {dark} of {fixture.PixelCount} pixels ({PixelRange.Format(ranges)}); they keep their channels and show black.";
        });

        /// <summary>The form's fields, validated as a fixture, or null with the status line saying what is wrong.</summary>
        private FormValues? ReadForm()
        {
            if (!TryReadInt(NewPixelCount, out int pixelCount) || pixelCount <= 0)
            {
                StatusText = "Pixel count must be a positive whole number.";
                return null;
            }
            if (!TryReadFloat(NewSpacing, out float spacing) || spacing <= 0f)
            {
                StatusText = "Spacing must be a positive number.";
                return null;
            }
            if (!TryReadFloat(NewAngle, out float angle))
            {
                StatusText = "Angle must be a number.";
                return null;
            }
            if (!TryReadInt(NewStartChannel, out int startChannel) || startChannel < 0)
            {
                StatusText = "Start channel must be zero or greater.";
                return null;
            }
            if (!TryReadInt(NewPort, out int port))
            {
                port = OutputTarget.DefaultDdpPort;
            }
            if (!TryReadInt(NewUniverse, out int universe) || universe < 0 || universe > 255)
            {
                universe = 0;
            }

            return new FormValues(
                (NewName ?? string.Empty).Trim(),
                new OutputTarget
                {
                    Interface = NewInterface,
                    IpAddress = (NewIpAddress ?? string.Empty).Trim(),
                    Port = port,
                    Universe = (byte)universe,
                    StartChannel = startChannel
                },
                string.IsNullOrWhiteSpace(NewPixelType) ? "WS2812B" : NewPixelType,
                pixelCount,
                spacing,
                angle);
        }

        private readonly record struct FormValues(
            string Name, OutputTarget Output, string PixelType, int PixelCount, float Spacing, float Angle);

        /// <summary>
        /// Fills the form from the selected fixture, so it can be edited and written back with
        /// <see cref="ApplyFormToSelectedCommand"/>. Spacing and angle are measured off the run,
        /// since the map does not store them.
        /// </summary>
        [RelayCommand]
        private void LoadSelectedIntoForm() => Guard("Load fixture", () =>
        {
            var fixture = SelectedFixture;
            if (fixture == null)
            {
                StatusText = "Select a fixture first.";
                return;
            }

            NewName = fixture.Name;
            NewInterface = fixture.Output.Interface;
            NewIpAddress = fixture.Output.IpAddress;
            NewPort = fixture.Output.Port.ToString(CultureInfo.InvariantCulture);
            NewUniverse = fixture.Output.Universe.ToString(CultureInfo.InvariantCulture);
            NewStartChannel = fixture.Output.StartChannel.ToString(CultureInfo.InvariantCulture);
            NewPixelType = fixture.PixelType;
            NewPixelCount = fixture.PixelCount.ToString(CultureInfo.InvariantCulture);
            float spacing = PixelLayoutTools.MeasureSpacing(fixture.PixelOffsets);
            NewSpacing = FormatSpacing(spacing > 0f ? spacing : 1.25f);
            NewAngle = FormatDegrees(HeadingOf(fixture));
            StatusText = $"Form holds '{fixture.Name}'. Edit it and press Apply to selected, or Add to make a copy.";
        });

        /// <summary>
        /// Writes the form onto the selected fixture: name, controller, start channel, pixel type
        /// and pixel count. The run stays where it is — a shorter count cuts it, a longer one
        /// carries it on past its last pixel — so spacing and angle here only lay out the pixels
        /// that are added; the Spacing and Bend boxes above change an existing run. Blanked
        /// pixels beyond a shorter run are dropped.
        /// </summary>
        [RelayCommand]
        private void ApplyFormToSelected() => Guard("Apply to fixture", () =>
        {
            var fixture = SelectedFixture;
            if (fixture == null)
            {
                StatusText = "Select a fixture first.";
                return;
            }

            var form = ReadForm();
            if (form == null)
            {
                return;
            }
            var values = form.Value;

            string previousName = fixture.Name;
            var previousOutput = fixture.Output;
            string previousType = fixture.PixelType;
            var previousOffsets = fixture.PixelOffsets;
            var previousBlanked = fixture.BlankedPixels;
            int previousCount = fixture.PixelCount;

            fixture.Name = string.IsNullOrWhiteSpace(values.Name) ? previousName : values.Name;
            fixture.Output = values.Output;
            fixture.PixelType = values.PixelType;
            if (values.PixelCount != previousCount)
            {
                fixture.PixelOffsets = PixelLayoutTools.SetPixelCount(previousOffsets, values.PixelCount, values.Spacing, values.Angle);
                fixture.BlankedPixels = PixelRange.ClipTo(previousBlanked, values.PixelCount);
            }

            if (!PublishWorking())
            {
                fixture.Name = previousName;
                fixture.Output = previousOutput;
                fixture.PixelType = previousType;
                fixture.PixelOffsets = previousOffsets;
                fixture.BlankedPixels = previousBlanked;
                return;
            }

            int channels = ChannelPlanner.ChannelCount(fixture, _pixelTypes);
            string range = channels > 0
                ? $" on channels {values.Output.StartChannel}–{values.Output.StartChannel + channels - 1} of {fixture.Output.Key}"
                : string.Empty;
            string resized = values.PixelCount == previousCount
                ? string.Empty
                : values.PixelCount > previousCount
                    ? $", extended from {previousCount} to {values.PixelCount} px"
                    : $", cut from {previousCount} to {values.PixelCount} px";
            StatusText = $"Updated '{fixture.Name}'{range}{resized}.";
        });

        [RelayCommand]
        private void AddFixture() => Guard("Add fixture", () =>
        {
            var form = ReadForm();
            if (form == null)
            {
                return;
            }
            var values = form.Value;

            var fixture = new FixtureDefinition
            {
                Name = string.IsNullOrWhiteSpace(values.Name) ? $"Fixture {Working.Fixtures.Count + 1}" : values.Name,
                Output = values.Output,
                PixelType = values.PixelType,
                // Spacing and angle are consumed here and not stored: the map keeps the resulting
                // positions, so editor tooling can grow without a schema change.
                PixelOffsets = PixelLayoutTools.GenerateLine(values.PixelCount, values.Spacing, values.Angle),
                OriginX = Working.ImageWidth / 4f,
                OriginY = Working.ImageHeight / 2f
            };
            int startChannel = values.Output.StartChannel;

            Working.Fixtures.Add(fixture);
            SelectedFixtureId = fixture.Id;

            if (PublishWorking())
            {
                int channels = ChannelPlanner.ChannelCount(fixture, _pixelTypes);
                string range = channels > 0
                    ? $" on channels {startChannel}–{startChannel + channels - 1} of {fixture.Output.Key}"
                    : string.Empty;
                StatusText = $"Added '{fixture.Name}'{range}. Drag it into place.";
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

        /// <summary>One decimal, and never "-0": a straightened run measures a hair under zero as often as over.</summary>
        private static string FormatDegrees(float degrees)
        {
            float rounded = MathF.Round(degrees, 1);
            if (rounded == 0f)
            {
                rounded = 0f;
            }
            return rounded.ToString("0.#", CultureInfo.InvariantCulture);
        }

        /// <summary>Heading of a fixture's first link in image space, for the status line.</summary>
        private static float HeadingOf(FixtureDefinition fixture)
        {
            var offsets = fixture.PixelOffsets;
            return offsets.Length >= 4
                ? PixelLayoutTools.HeadingDegrees(offsets[2] - offsets[0], offsets[3] - offsets[1])
                : 0f;
        }

        private static bool TryReadInt(string text, out int value) =>
            int.TryParse((text ?? string.Empty).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value);

        private static bool TryReadFloat(string text, out float value) =>
            float.TryParse((text ?? string.Empty).Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }
}
