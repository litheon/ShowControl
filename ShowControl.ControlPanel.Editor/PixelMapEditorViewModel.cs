using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
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

        [ObservableProperty]
        private string _statusText = string.Empty;

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
            ILogger<PixelMapEditorViewModel>? logger = null)
            : base(logger)
        {
            _editor = editor ?? throw new ArgumentNullException(nameof(editor));
            _pixelTypes = pixelTypes ?? throw new ArgumentNullException(nameof(pixelTypes));
            Zoom = new PreviewZoomViewModel(logger);
            Rows = new ObservableCollection<FixtureRow>();
            Working = editor.Current.Clone();
            Zoom.SetImageSize(Working.ImageWidth, Working.ImageHeight);
            RefreshRows();
            RefreshDirtyText();
        }

        /// <summary>Where the preview is looking. Presentation only; the rig samples the whole image.</summary>
        public PreviewZoomViewModel Zoom { get; }

        /// <summary>
        /// The map being edited. Applied to the session, which clones before publishing, so this
        /// instance is never the one the render thread is reading.
        /// </summary>
        public PixelMap Working { get; private set; }

        public ObservableCollection<FixtureRow> Rows { get; }

        public bool IsDragging => _dragFixtureId.HasValue;

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

        public FixtureDefinition? SelectedFixture => SelectedFixtureId.HasValue
            ? Working.Fixtures.FirstOrDefault(f => f.Id == SelectedFixtureId.Value)
            : null;

        // ------------------------------------------------------------------ lifecycle

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
        /// Starts a fixture drag if there is one under the point. Anchored on the press, so the
        /// grabbed point stays under the cursor however many moves are coalesced or dropped.
        /// </summary>
        public bool TryBeginDrag(double imageX, double imageY, double thresholdImageUnits)
        {
            var hit = FixtureHitTester.HitTest(Working, imageX, imageY, thresholdImageUnits);
            SelectedFixtureId = hit?.Id;
            _dragFixtureId = hit?.Id;

            if (hit == null)
            {
                return false;
            }

            _dragStartImageX = imageX;
            _dragStartImageY = imageY;
            _dragStartOriginX = hit.OriginX;
            _dragStartOriginY = hit.OriginY;
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
                _dragFixtureId = null;
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
                _dragFixtureId = null;
            }
        }

        /// <summary>Ends the drag and lets the panel catch up. Returns true if a drag was running.</summary>
        public bool EndDrag()
        {
            if (!_dragFixtureId.HasValue)
            {
                return false;
            }

            var dragged = Working.Fixtures.FirstOrDefault(f => f.Id == _dragFixtureId.Value);
            _dragFixtureId = null;
            RefreshRows();
            RefreshDirtyText();

            if (dragged != null && StatusText.StartsWith("Dragging", StringComparison.Ordinal))
            {
                StatusText = $"'{dragged.Name}' origin {dragged.OriginX:F0}, {dragged.OriginY:F0}.";
            }

            return true;
        }

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

        public IReadOnlyList<string> KnownPixelTypes => _pixelTypes.KnownPixelTypes;

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
