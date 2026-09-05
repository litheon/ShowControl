using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Threading.Tasks;
using ShowControl.ControlPanel.Editor;
using ShowControl.PixelMapping;
using Xunit;

namespace ShowControl.Tests
{
    /// <summary>
    /// Counts what reaches the session and can be told to reject, so the view-model's publish
    /// behaviour is observable. Wraps a real <see cref="PixelMapSession"/> rather than faking one,
    /// so validation and defensive cloning behave as they do live; only the disk is stubbed out.
    /// </summary>
    internal sealed class CountingPixelMapEditor : IPixelMapEditor
    {
        private readonly PixelMapSession _session;

        public CountingPixelMapEditor(PixelMap map)
        {
            _session = PixelMapSession.FromMap(map, new TestPixelTypes());
        }

        public int ApplyCount { get; private set; }

        public int SaveWorkingCopyCount { get; private set; }

        public bool RejectEdits { get; set; }

        /// <summary>Paths the view-model asked for, in order. Nothing reaches the disk.</summary>
        public List<string> SavedPaths { get; } = new List<string>();

        public List<string> OpenedPaths { get; } = new List<string>();

        public int KeptRecoveredWorkCount { get; private set; }

        public int DiscardedRecoveredWorkCount { get; private set; }

        /// <summary>Stands in for edits the session would have to be driven through to produce.</summary>
        public bool PretendDirty { get; set; }

        public bool PretendRecoveredUnsavedWork { get; set; }

        public PixelMap Current => _session.Current;

        public PixelSampleMap CurrentSampleMap => _session.CurrentSampleMap;

        public PixelMapLayout CurrentLayout => _session.CurrentLayout;

        public PixelMapSummary CurrentSummary => _session.CurrentSummary;

        public bool IsDirty => PretendDirty || _session.IsDirty;

        public string? SavedPath => SavedPaths.LastOrDefault() ?? _session.SavedPath;

        public bool RecoveredUnsavedWork => PretendRecoveredUnsavedWork || _session.RecoveredUnsavedWork;

        public event EventHandler<PixelMapChangedEventArgs>? Changed
        {
            add => _session.Changed += value;
            remove => _session.Changed -= value;
        }

        public IReadOnlyList<string> Apply(PixelMap updated)
        {
            ApplyCount++;
            return RejectEdits ? new[] { "rejected by the test" } : _session.Apply(updated);
        }

        public void SaveWorkingCopy() => SaveWorkingCopyCount++;

        public void Save(string path) => SavedPaths.Add(path);

        public PixelMap Open(string path)
        {
            OpenedPaths.Add(path);
            return _session.Current;
        }

        public void KeepRecoveredWork()
        {
            KeptRecoveredWorkCount++;
            PretendRecoveredUnsavedWork = false;
        }

        public void DiscardRecoveredWork()
        {
            DiscardedRecoveredWorkCount++;
            PretendRecoveredUnsavedWork = false;
        }
    }

    /// <summary>
    /// Answers the editor's questions with whatever the test says, and records what it was asked.
    /// The point of the dialog interfaces is that this is possible with no UI in the process.
    /// </summary>
    internal sealed class ScriptedDialogs : IPixelMapFileDialog, IEditorDialogs
    {
        public string? OpenPath { get; set; }

        public string? SaveAsPath { get; set; }

        public bool ConfirmDiscard { get; set; } = true;

        public RecoveredWorkChoice RecoveredWorkAnswer { get; set; } = RecoveredWorkChoice.Keep;

        public int ConfirmDiscardCount { get; private set; }

        public int RecoveredWorkPromptCount { get; private set; }

        public int SaveAsPromptCount { get; private set; }

        public string? SuggestedFileName { get; private set; }

        public string? ExportedContents { get; private set; }

        public Task<string?> PickOpenPathAsync() => Task.FromResult(OpenPath);

        public Task<string?> SaveAsAsync(string suggestedFileName, string contents)
        {
            SaveAsPromptCount++;
            SuggestedFileName = suggestedFileName;
            ExportedContents = contents;
            return Task.FromResult(SaveAsPath);
        }

        public Task<bool> ConfirmDiscardingUnsavedChangesAsync()
        {
            ConfirmDiscardCount++;
            return Task.FromResult(ConfirmDiscard);
        }

        public Task<RecoveredWorkChoice> AskAboutRecoveredWorkAsync()
        {
            RecoveredWorkPromptCount++;
            return Task.FromResult(RecoveredWorkAnswer);
        }
    }

    public class PixelMapEditorViewModelTests
    {
        private static (PixelMapEditorViewModel ViewModel, CountingPixelMapEditor Editor) NewEditor(
            params FixtureDefinition[] fixtures)
        {
            var map = PixelMappingTestData.MapOf(fixtures);
            var editor = new CountingPixelMapEditor(map);
            return (new PixelMapEditorViewModel(editor, new TestPixelTypes()), editor);
        }

        private static (PixelMapEditorViewModel ViewModel, CountingPixelMapEditor Editor, ScriptedDialogs Dialogs)
            NewEditorWithDialogs(params FixtureDefinition[] fixtures)
        {
            var map = PixelMappingTestData.MapOf(fixtures);
            var editor = new CountingPixelMapEditor(map);
            var dialogs = new ScriptedDialogs();
            return (new PixelMapEditorViewModel(editor, new TestPixelTypes(), logger: null, dialogs, dialogs), editor, dialogs);
        }

        /// <summary>
        /// The invariant the whole drag rests on: every move publishes — that is what makes the
        /// lights follow the cursor — and nothing the panel or the fixture list is bound to is
        /// touched while it runs. Rebuilding the list at pointer rate is both wasteful and, on Mac
        /// Catalyst, a crash.
        /// </summary>
        [Fact]
        public void DraggingAFixture_PublishesEveryMove_AndLeavesTheBoundListAlone()
        {
            var (vm, editor) = NewEditor(PixelMappingTestData.Strip("Strip", originX: 100f, originY: 100f, pixelCount: 4));

            int rowChanges = 0;
            ((INotifyCollectionChanged)vm.Rows).CollectionChanged += (s, e) => rowChanges++;

            Assert.True(vm.TryBeginDrag(100d, 100d, thresholdImageUnits: 5d));
            int appliesAfterBegin = editor.ApplyCount;
            string statusAtBegin = vm.StatusText;

            for (int step = 1; step <= 5; step++)
            {
                vm.DragTo(100d + step, 100d);
            }

            Assert.Equal(appliesAfterBegin + 5, editor.ApplyCount);
            Assert.Equal(0, rowChanges);
            Assert.Equal(statusAtBegin, vm.StatusText);
            Assert.Equal(105f, vm.Working.Fixtures[0].OriginX);
        }

        /// <summary>
        /// Anchored on the press rather than accumulated, so a coalesced or dropped move cannot
        /// leave the fixture drifting behind the cursor.
        /// </summary>
        [Fact]
        public void ASkippedMove_LandsTheFixtureWhereAContinuousDragWouldHave()
        {
            var (continuous, _) = NewEditor(PixelMappingTestData.Strip("A", originX: 10f, originY: 20f, pixelCount: 4));
            var (skipped, _) = NewEditor(PixelMappingTestData.Strip("A", originX: 10f, originY: 20f, pixelCount: 4));

            continuous.TryBeginDrag(10d, 20d, 5d);
            foreach (int step in new[] { 1, 2, 3, 4, 5 })
            {
                continuous.DragTo(10d + step, 20d + step);
            }

            skipped.TryBeginDrag(10d, 20d, 5d);
            skipped.DragTo(15d, 25d);

            Assert.Equal(continuous.Working.Fixtures[0].OriginX, skipped.Working.Fixtures[0].OriginX);
            Assert.Equal(continuous.Working.Fixtures[0].OriginY, skipped.Working.Fixtures[0].OriginY);
        }

        [Fact]
        public void ARejectedMove_PutsTheFixtureBack_AndStopsTheDrag()
        {
            var (vm, editor) = NewEditor(PixelMappingTestData.Strip("A", originX: 40f, originY: 40f, pixelCount: 4));
            vm.TryBeginDrag(40d, 40d, 5d);

            editor.RejectEdits = true;
            vm.DragTo(90d, 90d);

            Assert.Equal(40f, vm.Working.Fixtures[0].OriginX);
            Assert.Equal(40f, vm.Working.Fixtures[0].OriginY);
            Assert.False(vm.IsDragging);
        }

        [Fact]
        public void PressingEmptySpace_SelectsNothing_AndStartsNoDrag()
        {
            var (vm, _) = NewEditor(PixelMappingTestData.Strip("A", originX: 10f, originY: 10f, pixelCount: 4));

            Assert.False(vm.TryBeginDrag(900d, 900d, thresholdImageUnits: 5d));
            Assert.Null(vm.SelectedFixtureId);
            Assert.False(vm.IsDragging);
        }

        [Fact]
        public void EndingADrag_BringsTheListBackInStep_AndReportsWhereTheFixtureLanded()
        {
            var (vm, _) = NewEditor(PixelMappingTestData.Strip("Strip 1", originX: 100f, originY: 100f, pixelCount: 4));
            vm.TryBeginDrag(100d, 100d, 5d);
            vm.DragTo(150d, 120d);

            Assert.True(vm.EndDrag());

            Assert.False(vm.IsDragging);
            Assert.Contains("150", vm.StatusText);
            Assert.False(vm.EndDrag()); // idempotent: a pan or a stray release must not re-run it
        }

        /// <summary>
        /// The scoped press: the first drag on a fixture selects it and moves the whole run; a
        /// press on the fixture that is already selected grabs the pixel under the cursor, and the
        /// run follows it as an inextensible rope — neighbours come along when their link is
        /// pulled taut, and no link ever stretches. Same invariants as the whole-fixture drag —
        /// every move publishes, nothing the panel is bound to is touched until the drag ends.
        /// </summary>
        [Fact]
        public void PressingTheSelectedFixture_GrabsThePixelUnderTheCursor_AndTheRunFollowsWhenPulledTaut()
        {
            var (vm, editor) = NewEditor(PixelMappingTestData.Strip("Strip", originX: 100f, originY: 100f, pixelCount: 10));

            int rowChanges = 0;
            ((INotifyCollectionChanged)vm.Rows).CollectionChanged += (s, e) => rowChanges++;

            // First press: selects, drags the whole fixture.
            Assert.True(vm.TryBeginDrag(100d, 100d, thresholdImageUnits: 5d));
            Assert.Null(vm.DraggedPixelIndex);
            vm.EndDrag();

            // Second press, on the now-selected fixture: grabs pixel 5.
            Assert.True(vm.TryBeginDrag(105d, 100d, thresholdImageUnits: 2d));
            Assert.Equal(5, vm.DraggedPixelIndex);
            int appliesAfterBegin = editor.ApplyCount;

            vm.DragTo(105d, 103d);

            Assert.Equal(appliesAfterBegin + 1, editor.ApplyCount);
            Assert.Equal(0, rowChanges);

            var fixture = vm.Working.Fixtures[0];
            var grabbed = PixelMapGeometry.Resolve(fixture, 5);
            Assert.Equal(105f, grabbed.X, 2);
            Assert.Equal(103f, grabbed.Y, 2);

            // The rope never stretches: no link is longer than the strip's 1-unit spacing, and the
            // two links at the grab are drawn exactly taut.
            var positions = PixelMapGeometry.Resolve(fixture);
            for (int i = 1; i < positions.Length; i++)
            {
                float dx = positions[i].X - positions[i - 1].X;
                float dy = positions[i].Y - positions[i - 1].Y;
                Assert.InRange(MathF.Sqrt((dx * dx) + (dy * dy)), 0f, 1.001f);
            }
            float grabLinkBefore = MathF.Sqrt(
                MathF.Pow(positions[5].X - positions[4].X, 2) + MathF.Pow(positions[5].Y - positions[4].Y, 2));
            float grabLinkAfter = MathF.Sqrt(
                MathF.Pow(positions[6].X - positions[5].X, 2) + MathF.Pow(positions[6].Y - positions[5].Y, 2));
            Assert.Equal(1f, grabLinkBefore, 3);
            Assert.Equal(1f, grabLinkAfter, 3);

            // The origin did not move: the shape changed, not the fixture's anchor.
            Assert.Equal(100f, fixture.OriginX);
            Assert.Equal(100f, fixture.OriginY);

            Assert.True(vm.EndDrag());
            Assert.Null(vm.DraggedPixelIndex);
            Assert.Contains("pixel 5", vm.StatusText);
        }

        /// <summary>
        /// Dragging pixel 0 carries it off the origin; ending the drag rebases so the origin marks
        /// pixel 0 again, moving nothing — the same positions expressed by convention.
        /// </summary>
        [Fact]
        public void DraggingPixelZero_RebasesOnEnd_SoTheOriginStillMarksPixelZero()
        {
            var (vm, _) = NewEditor(PixelMappingTestData.Strip("Strip", originX: 100f, originY: 100f, pixelCount: 6));

            vm.TryBeginDrag(100d, 100d, 5d); // select + whole drag
            vm.EndDrag();
            vm.TryBeginDrag(100d, 100d, 2d); // pixel 0 drag
            Assert.Equal(0, vm.DraggedPixelIndex);

            vm.DragTo(97d, 98d);
            var fixture = vm.Working.Fixtures[0];
            var positionsBeforeEnd = PixelMapGeometry.Resolve(fixture);

            vm.EndDrag();

            Assert.Equal(0f, fixture.PixelOffsets[0]);
            Assert.Equal(0f, fixture.PixelOffsets[1]);
            Assert.Equal(97f, fixture.OriginX, 2);
            Assert.Equal(98f, fixture.OriginY, 2);

            var positionsAfterEnd = PixelMapGeometry.Resolve(fixture);
            for (int i = 0; i < positionsBeforeEnd.Length; i++)
            {
                Assert.Equal(positionsBeforeEnd[i].X, positionsAfterEnd[i].X, 2);
                Assert.Equal(positionsBeforeEnd[i].Y, positionsAfterEnd[i].Y, 2);
            }
        }

        /// <summary>
        /// Anchored on the press exactly as the whole-fixture drag is: recomputed from the rest
        /// shape, so coalesced or dropped moves land the pixel in the same place.
        /// </summary>
        [Fact]
        public void ASkippedMove_LandsThePixelWhereAContinuousDragWouldHave()
        {
            var (continuous, _) = NewEditor(PixelMappingTestData.Strip("A", originX: 10f, originY: 20f, pixelCount: 8));
            var (skipped, _) = NewEditor(PixelMappingTestData.Strip("A", originX: 10f, originY: 20f, pixelCount: 8));

            foreach (var vm in new[] { continuous, skipped })
            {
                vm.TryBeginDrag(13d, 20d, 5d); // select
                vm.EndDrag();
                vm.TryBeginDrag(13d, 20d, 2d); // grab pixel 3
                Assert.Equal(3, vm.DraggedPixelIndex);
            }

            for (int step = 1; step <= 4; step++)
            {
                continuous.DragTo(13d + step, 20d + step);
            }
            skipped.DragTo(17d, 24d);

            Assert.Equal(continuous.Working.Fixtures[0].PixelOffsets, skipped.Working.Fixtures[0].PixelOffsets);
        }

        [Fact]
        public void PressingADifferentFixture_MovesItWhole_NotByThePixel()
        {
            var a = PixelMappingTestData.Strip("A", originX: 10f, originY: 10f, pixelCount: 4);
            var b = PixelMappingTestData.Strip("B", originX: 50f, originY: 50f, pixelCount: 4, startChannel: 100);
            var (vm, _) = NewEditor(a, b);

            vm.TryBeginDrag(10d, 10d, 5d); // select A
            vm.EndDrag();

            Assert.True(vm.TryBeginDrag(50d, 50d, 5d)); // press B: a fresh selection, so a whole drag
            Assert.Equal(b.Id, vm.SelectedFixtureId);
            Assert.Null(vm.DraggedPixelIndex);
        }

        [Fact]
        public void ARejectedPixelMove_PutsTheShapeBack_AndStopsTheDrag()
        {
            var (vm, editor) = NewEditor(PixelMappingTestData.Strip("A", originX: 40f, originY: 40f, pixelCount: 6));
            vm.TryBeginDrag(40d, 40d, 5d);
            vm.EndDrag();
            vm.TryBeginDrag(42d, 40d, 2d);
            Assert.Equal(2, vm.DraggedPixelIndex);
            var shapeBefore = (float[])vm.Working.Fixtures[0].PixelOffsets.Clone();

            editor.RejectEdits = true;
            vm.DragTo(60d, 60d);

            Assert.Equal(shapeBefore, vm.Working.Fixtures[0].PixelOffsets);
            Assert.False(vm.IsDragging);
            Assert.Null(vm.DraggedPixelIndex);
        }

        [Fact]
        public void SelectingAFixture_ShowsTheSpacingMeasuredFromItsOffsets()
        {
            var fixture = PixelMappingTestData.Strip("A", 0f, 0f, pixelCount: 5);
            PixelLayoutTools.SetSpacing(fixture.PixelOffsets, 2.5f);
            var (vm, _) = NewEditor(fixture);

            vm.SelectedFixtureId = fixture.Id;

            Assert.Equal("2.5", vm.SpacingText);
        }

        [Fact]
        public void ApplyingSpacing_RespacesTheFixture_AndPublishesIt()
        {
            var fixture = PixelMappingTestData.Strip("A", 0f, 0f, pixelCount: 5);
            var (vm, editor) = NewEditor(fixture);
            vm.SelectedFixtureId = fixture.Id;
            int appliesBefore = editor.ApplyCount;

            vm.SpacingText = "3";
            vm.ApplySpacingCommand.Execute(null);

            Assert.Equal(3f, PixelLayoutTools.MeasureSpacing(vm.Working.Fixtures[0].PixelOffsets), 4);
            Assert.Equal(appliesBefore + 1, editor.ApplyCount);
        }

        [Theory]
        [InlineData("")]
        [InlineData("0")]
        [InlineData("-2")]
        [InlineData("wide")]
        public void ApplyingANonPositiveSpacing_IsRefusedWithoutTouchingTheFixture(string text)
        {
            var fixture = PixelMappingTestData.Strip("A", 0f, 0f, pixelCount: 5);
            var (vm, editor) = NewEditor(fixture);
            vm.SelectedFixtureId = fixture.Id;
            var before = (float[])vm.Working.Fixtures[0].PixelOffsets.Clone();
            int appliesBefore = editor.ApplyCount;

            vm.SpacingText = text;
            vm.ApplySpacingCommand.Execute(null);

            Assert.Equal(before, vm.Working.Fixtures[0].PixelOffsets);
            Assert.Equal(appliesBefore, editor.ApplyCount);
            Assert.Contains("positive number", vm.StatusText);
        }

        [Fact]
        public void FlippingDirection_ReversesTheRunInPlace()
        {
            var fixture = PixelMappingTestData.Strip("A", originX: 10f, originY: 10f, pixelCount: 4);
            var (vm, _) = NewEditor(fixture);
            vm.SelectedFixtureId = fixture.Id;
            var before = PixelMapGeometry.Resolve(vm.Working.Fixtures[0]);

            vm.FlipDirectionCommand.Execute(null);

            var after = PixelMapGeometry.Resolve(vm.Working.Fixtures[0]);
            for (int i = 0; i < before.Length; i++)
            {
                Assert.Equal(before[before.Length - 1 - i], after[i]);
            }
        }

        [Fact]
        public void CommandsWithNothingSelected_SayWhatIsMissingRatherThanThrowing()
        {
            var (vm, _) = NewEditor(PixelMappingTestData.Strip("A", 0f, 0f, pixelCount: 4));

            foreach (var command in new[] { vm.ToggleEnabledCommand, vm.RemoveFixtureCommand, vm.FlipDirectionCommand, vm.ApplySpacingCommand })
            {
                vm.StatusText = string.Empty;
                command.Execute(null);
                Assert.Equal("Select a fixture first.", vm.StatusText);
            }
        }

        [Fact]
        public void AddingAFixture_AppendsARowAndSelectsIt()
        {
            var (vm, _) = NewEditor(PixelMappingTestData.Strip("A", 0f, 0f, pixelCount: 4, startChannel: 0));

            vm.NewName = "Strip 2";
            vm.NewPixelCount = "10";
            vm.NewStartChannel = "12";
            vm.AddFixtureCommand.Execute(null);

            Assert.Equal(2, vm.Working.Fixtures.Count);
            Assert.Equal(2, vm.Rows.Count);
            Assert.Equal(vm.Working.Fixtures[1].Id, vm.SelectedFixtureId);
            Assert.Contains("Added 'Strip 2'", vm.StatusText);
        }

        [Theory]
        [InlineData("0", "Pixel count")]
        [InlineData("-4", "Pixel count")]
        [InlineData("many", "Pixel count")]
        public void AddingAFixtureWithABadPixelCount_IsRefused(string pixelCount, string expected)
        {
            var (vm, _) = NewEditor(PixelMappingTestData.Strip("A", 0f, 0f, pixelCount: 4));

            vm.NewPixelCount = pixelCount;
            vm.AddFixtureCommand.Execute(null);

            Assert.Single(vm.Working.Fixtures);
            Assert.Contains(expected, vm.StatusText);
        }

        [Fact]
        public void RemovingAFixture_DropsItsRowAndClearsTheSelection()
        {
            var first = PixelMappingTestData.Strip("A", 0f, 0f, pixelCount: 4, startChannel: 0);
            var second = PixelMappingTestData.Strip("B", 50f, 0f, pixelCount: 4, startChannel: 12);
            var (vm, _) = NewEditor(first, second);
            vm.SelectedFixtureId = second.Id;

            vm.RemoveFixtureCommand.Execute(null);

            Assert.Single(vm.Working.Fixtures);
            Assert.Single(vm.Rows);
            Assert.Null(vm.SelectedFixtureId);
        }

        /// <summary>
        /// An unchanged rig has to leave the row collection completely alone — every change it
        /// raises is an index-path update on the platform list, and those are what abort the app.
        /// </summary>
        [Fact]
        public void RefreshingRowsWithNothingChanged_RaisesNoCollectionChange()
        {
            var (vm, _) = NewEditor(
                PixelMappingTestData.Strip("A", 0f, 0f, pixelCount: 4, startChannel: 0),
                PixelMappingTestData.Strip("B", 50f, 0f, pixelCount: 4, startChannel: 12));

            int changes = 0;
            ((INotifyCollectionChanged)vm.Rows).CollectionChanged += (s, e) => changes++;

            vm.RefreshRows();
            vm.RefreshRows();

            Assert.Equal(0, changes);
        }

        [Fact]
        public void TogglingAFixture_UpdatesOnlyItsOwnRow()
        {
            var first = PixelMappingTestData.Strip("A", 0f, 0f, pixelCount: 4, startChannel: 0);
            var second = PixelMappingTestData.Strip("B", 50f, 0f, pixelCount: 4, startChannel: 12);
            var (vm, _) = NewEditor(first, second);
            vm.SelectedFixtureId = second.Id;

            var changed = new List<NotifyCollectionChangedAction>();
            ((INotifyCollectionChanged)vm.Rows).CollectionChanged += (s, e) => changed.Add(e.Action);

            vm.ToggleEnabledCommand.Execute(null);

            Assert.Equal(new[] { NotifyCollectionChangedAction.Replace }, changed);
            Assert.False(vm.Rows[1].Enabled);
            Assert.True(vm.Rows[0].Enabled);
        }

        // ------------------------------------------------------------------ edit mode

        /// <summary>
        /// Switching the editor on used to be a run of assignments in the switch's Toggled handler,
        /// so it only happened when the switch was what moved. It hangs off the property now, which
        /// is what lets the switch be a plain binding.
        /// </summary>
        [Fact]
        public void TurningEditModeOn_AdoptsWhateverTheRigIsRunning()
        {
            var (vm, editor) = NewEditor(PixelMappingTestData.Strip("A", originX: 10f, originY: 10f, pixelCount: 4));
            var behindOurBack = editor.Current.Clone();
            behindOurBack.Fixtures[0].OriginX = 400f;
            editor.Apply(behindOurBack);

            vm.IsEditing = true;

            Assert.Equal(400f, vm.Working.Fixtures[0].OriginX);
        }

        [Fact]
        public void TurningEditModeOff_ReturnsThePreviewToFit_AndAsksForAFlush()
        {
            var (vm, _) = NewEditor(PixelMappingTestData.Strip("A", 0f, 0f, pixelCount: 4));
            vm.IsEditing = true;
            vm.Zoom.ZoomAbout(4d, 0.25d, 0.25d);

            int flushes = 0;
            vm.FlushRequested += (s, e) => flushes++;

            vm.IsEditing = false;

            Assert.Equal(PreviewZoomViewModel.MinZoom, vm.Zoom.Zoom);
            Assert.Equal(PreviewRegion.WholeImage(vm.Working.ImageWidth, vm.Working.ImageHeight), vm.Zoom.Region);
            Assert.Equal(1, flushes);
        }

        /// <summary>
        /// The preview goes back to fit while the editor is off — a magnified picture with no
        /// visible way to undo it is a trap mid-show — but the operator's place is not lost.
        /// </summary>
        [Fact]
        public void ReEnteringEditMode_PutsTheViewBackWhereItWasLeft()
        {
            var (vm, _) = NewEditor(PixelMappingTestData.Strip("A", 0f, 0f, pixelCount: 4));
            vm.IsEditing = true;
            vm.Zoom.ZoomAbout(4d, 0.25d, 0.25d);
            double zoom = vm.Zoom.Zoom;
            double anchorX = vm.Zoom.AnchorX;
            double anchorY = vm.Zoom.AnchorY;

            vm.IsEditing = false;
            vm.IsEditing = true;

            Assert.Equal(zoom, vm.Zoom.Zoom, 6);
            Assert.Equal(anchorX, vm.Zoom.AnchorX, 6);
            Assert.Equal(anchorY, vm.Zoom.AnchorY, 6);
        }

        [Fact]
        public void TurningEditModeOff_DropsAGestureLeftRunning()
        {
            var fixture = PixelMappingTestData.Strip("A", originX: 100f, originY: 100f, pixelCount: 4);
            var (vm, _) = NewEditor(fixture);
            vm.IsEditing = true;
            vm.BeginGesture(100d, 100d, thresholdImageUnits: 5d);
            Assert.True(vm.IsDragging);

            vm.IsEditing = false;

            Assert.False(vm.IsDragging);
            Assert.False(vm.IsPanning);
        }

        [Fact]
        public void IsNotEditing_IsTheInverse_SoTheOverlayCanBindItsInputTransparency()
        {
            var (vm, _) = NewEditor(PixelMappingTestData.Strip("A", 0f, 0f, pixelCount: 4));

            Assert.True(vm.IsNotEditing);
            vm.IsEditing = true;
            Assert.False(vm.IsNotEditing);
        }

        // ------------------------------------------------------------------ panning

        /// <summary>
        /// A press with nothing under it moves the view instead, and moving the view is not an edit:
        /// nothing is published and the map is not touched.
        /// </summary>
        [Fact]
        public void PressingEmptySpace_PansTheViewWithoutPublishing()
        {
            var (vm, editor) = NewEditor(PixelMappingTestData.Strip("A", originX: 10f, originY: 10f, pixelCount: 4));
            vm.Zoom.ZoomAbout(4d, 0.5d, 0.5d);
            double startX = vm.Zoom.Region.X;
            int appliesBefore = editor.ApplyCount;

            vm.BeginGesture(900d, 900d, thresholdImageUnits: 5d);
            Assert.True(vm.IsPanning);
            Assert.False(vm.IsDragging);

            // Dragging the picture 50 image units to the left moves the view 50 to the right.
            vm.PanTo(850d, 900d);

            Assert.Equal(startX + 50d, vm.Zoom.Region.X, 3);
            Assert.Equal(appliesBefore, editor.ApplyCount);
            Assert.False(vm.EndGesture()); // a pan is not a drag, so nothing to bring back in step
            Assert.False(vm.IsPanning);
        }

        [Fact]
        public void AFixtureUnderThePress_TakesThePointerRatherThanThePan()
        {
            var fixture = PixelMappingTestData.Strip("A", originX: 100f, originY: 100f, pixelCount: 4);
            var (vm, _) = NewEditor(fixture);

            vm.BeginGesture(100d, 100d, thresholdImageUnits: 5d);

            Assert.True(vm.IsDragging);
            Assert.False(vm.IsPanning);
            Assert.Equal(fixture.Id, vm.SelectedFixtureId);
        }

        [Fact]
        public void APanLeftRunning_IsIgnoredOnceItHasEnded()
        {
            var (vm, _) = NewEditor(PixelMappingTestData.Strip("A", 0f, 0f, pixelCount: 4));
            vm.Zoom.ZoomAbout(4d, 0.5d, 0.5d);
            vm.BeginGesture(900d, 900d, thresholdImageUnits: 5d);
            vm.EndPan();

            double before = vm.Zoom.Region.X;
            vm.PanTo(100d, 900d);

            Assert.Equal(before, vm.Zoom.Region.X, 6);
        }

        /// <summary>
        /// A drag publishes on every move, so by the time it ends the working copy is well ahead of
        /// the file. The view owns the timer; the view-model is what knows an edit happened.
        /// </summary>
        [Fact]
        public void EndingADrag_AsksForAFlush()
        {
            var (vm, _) = NewEditor(PixelMappingTestData.Strip("A", originX: 100f, originY: 100f, pixelCount: 4));
            int flushes = 0;
            vm.FlushRequested += (s, e) => flushes++;

            vm.BeginGesture(100d, 100d, 5d);
            vm.DragTo(150d, 100d);
            Assert.True(vm.EndGesture());

            Assert.Equal(1, flushes);
            Assert.False(vm.EndGesture());
            Assert.Equal(1, flushes); // idempotent, so a stray release does not write again
        }

        // ------------------------------------------------------------------ file commands

        [Fact]
        public async Task Opening_WithUnsavedChanges_AsksFirst_AndHonoursACancel()
        {
            var (vm, editor, dialogs) = NewEditorWithDialogs(PixelMappingTestData.Strip("A", 0f, 0f, pixelCount: 4));
            editor.PretendDirty = true;
            dialogs.ConfirmDiscard = false;
            dialogs.OpenPath = "/tmp/other.json";

            await vm.OpenCommand.ExecuteAsync(null);

            Assert.Equal(1, dialogs.ConfirmDiscardCount);
            Assert.Empty(editor.OpenedPaths);
        }

        [Fact]
        public async Task Opening_WithNothingUnsaved_DoesNotAsk()
        {
            var (vm, editor, dialogs) = NewEditorWithDialogs(PixelMappingTestData.Strip("A", 0f, 0f, pixelCount: 4));
            dialogs.OpenPath = "/tmp/other.json";

            await vm.OpenCommand.ExecuteAsync(null);

            Assert.Equal(0, dialogs.ConfirmDiscardCount);
            Assert.Equal(new[] { "/tmp/other.json" }, editor.OpenedPaths);
            Assert.Contains("/tmp/other.json", vm.StatusText);
        }

        [Fact]
        public async Task CancellingTheFilePicker_ChangesNothing()
        {
            var (vm, editor, dialogs) = NewEditorWithDialogs(PixelMappingTestData.Strip("A", 0f, 0f, pixelCount: 4));
            dialogs.OpenPath = null;

            await vm.OpenCommand.ExecuteAsync(null);

            Assert.Empty(editor.OpenedPaths);
        }

        /// <summary>A map that has never been saved has nowhere to go, so Save has to ask where.</summary>
        [Fact]
        public async Task SavingAMapWithNoPathYet_GoesThroughSaveAs()
        {
            var (vm, editor, dialogs) = NewEditorWithDialogs(PixelMappingTestData.Strip("A", 0f, 0f, pixelCount: 4));
            dialogs.SaveAsPath = "/tmp/rig.json";

            await vm.SaveCommand.ExecuteAsync(null);

            Assert.Equal("pixelmap.json", dialogs.SuggestedFileName);
            Assert.Equal(new[] { "/tmp/rig.json" }, editor.SavedPaths);
        }

        [Fact]
        public async Task SavingAgain_GoesBackToThePathSaveAsChose()
        {
            var (vm, editor, dialogs) = NewEditorWithDialogs(PixelMappingTestData.Strip("A", 0f, 0f, pixelCount: 4));
            dialogs.SaveAsPath = "/tmp/rig.json";
            await vm.SaveAsCommand.ExecuteAsync(null);

            dialogs.SaveAsPath = "/tmp/should-not-be-asked.json";
            await vm.SaveCommand.ExecuteAsync(null);

            Assert.Equal(new[] { "/tmp/rig.json", "/tmp/rig.json" }, editor.SavedPaths);
            Assert.Equal(1, dialogs.SaveAsPromptCount); // the second Save never asked
        }

        /// <summary>Save As on a map that has a home offers that file's name back.</summary>
        [Fact]
        public async Task SaveAs_SuggestsTheNameOfTheFileTheMapCameFrom()
        {
            var (vm, _, dialogs) = NewEditorWithDialogs(PixelMappingTestData.Strip("A", 0f, 0f, pixelCount: 4));
            dialogs.SaveAsPath = "/tmp/rig.json";
            await vm.SaveAsCommand.ExecuteAsync(null);

            await vm.SaveAsCommand.ExecuteAsync(null);

            Assert.Equal("rig.json", dialogs.SuggestedFileName);
        }

        [Fact]
        public async Task CancellingSaveAs_RecordsNoPath()
        {
            var (vm, editor, dialogs) = NewEditorWithDialogs(PixelMappingTestData.Strip("A", 0f, 0f, pixelCount: 4));
            dialogs.SaveAsPath = null;

            await vm.SaveAsCommand.ExecuteAsync(null);

            Assert.Empty(editor.SavedPaths);
            Assert.NotNull(dialogs.ExportedContents); // the map was serialized for the picker
        }

        [Theory]
        [InlineData(RecoveredWorkChoice.Keep, 1, 0, 0)]
        [InlineData(RecoveredWorkChoice.Discard, 0, 1, 0)]
        [InlineData(RecoveredWorkChoice.SaveAs, 1, 0, 1)]
        public async Task RecoveredWork_IsKeptDiscardedOrSaved_AsAnswered(
            RecoveredWorkChoice answer, int kept, int discarded, int saved)
        {
            var (vm, editor, dialogs) = NewEditorWithDialogs(PixelMappingTestData.Strip("A", 0f, 0f, pixelCount: 4));
            editor.PretendRecoveredUnsavedWork = true;
            dialogs.RecoveredWorkAnswer = answer;
            dialogs.SaveAsPath = "/tmp/recovered.json";

            await vm.ReviewRecoveredWorkCommand.ExecuteAsync(null);

            Assert.Equal(1, dialogs.RecoveredWorkPromptCount);
            Assert.Equal(kept, editor.KeptRecoveredWorkCount);
            Assert.Equal(discarded, editor.DiscardedRecoveredWorkCount);
            Assert.Equal(saved, editor.SavedPaths.Count);
        }

        [Fact]
        public async Task WithNothingRecovered_NobodyIsAsked()
        {
            var (vm, _, dialogs) = NewEditorWithDialogs(PixelMappingTestData.Strip("A", 0f, 0f, pixelCount: 4));

            await vm.ReviewRecoveredWorkCommand.ExecuteAsync(null);

            Assert.Equal(0, dialogs.RecoveredWorkPromptCount);
        }

        /// <summary>
        /// A view-model built without dialogs — a test, or a host that has none — must say so rather
        /// than throw out of a command body, which on Mac Catalyst aborts the process.
        /// </summary>
        [Fact]
        public async Task FileCommandsWithNoDialogs_SaySoInsteadOfThrowing()
        {
            var (vm, _) = NewEditor(PixelMappingTestData.Strip("A", 0f, 0f, pixelCount: 4));

            await vm.OpenCommand.ExecuteAsync(null);
            Assert.Contains("dialog", vm.StatusText);

            vm.StatusText = string.Empty;
            await vm.SaveAsCommand.ExecuteAsync(null);
            Assert.Contains("dialog", vm.StatusText);
        }

        // ------------------------------------------------------------------ option lists

        [Fact]
        public void TheOptionListsCoverEveryChoice_AndAreNotFilledInByTheView()
        {
            var (vm, _) = NewEditor(PixelMappingTestData.Strip("A", 0f, 0f, pixelCount: 4));

            // Taken from the enum, so a third interface cannot go missing from the panel.
            Assert.Equal(Enum.GetValues<OutputInterfaceType>(), vm.InterfaceOptions);
            Assert.Equal(
                vm.InterfaceOptions.Select(option => option.ToString()),
                vm.InterfaceOptionRows.Select(row => row.Label));
            Assert.Equal(vm.KnownPixelTypes, vm.PixelTypeOptionRows.Select(row => row.Label));
        }

        /// <summary>
        /// Clicking a row is the whole interaction — there is no picker and no popup — so it has to
        /// set the value the next fixture is built from.
        /// </summary>
        [Fact]
        public void ClickingAnOptionRow_SetsTheValueTheFixtureIsBuiltFrom()
        {
            var (vm, _) = NewEditor(PixelMappingTestData.Strip("A", 0f, 0f, pixelCount: 4));

            RowFor(vm.InterfaceOptionRows, nameof(OutputInterfaceType.ArtNet)).ChooseCommand.Execute(null);
            RowFor(vm.PixelTypeOptionRows, "Sheds").ChooseCommand.Execute(null);

            Assert.Equal(OutputInterfaceType.ArtNet, vm.NewInterface);
            Assert.Equal("Sheds", vm.NewPixelType);

            vm.AddFixtureCommand.Execute(null);
            var added = vm.Working.Fixtures[^1];
            Assert.Equal(OutputInterfaceType.ArtNet, added.Output.Interface);
            Assert.Equal("Sheds", added.PixelType);
        }

        /// <summary>
        /// The rows show the current choice rather than holding it, so a value set from anywhere —
        /// a click, a test, a future "duplicate this fixture" — moves the mark.
        /// </summary>
        [Fact]
        public void TheMarkedRow_FollowsTheValue_AndOnlyOneIsMarked()
        {
            var (vm, _) = NewEditor(PixelMappingTestData.Strip("A", 0f, 0f, pixelCount: 4));

            Assert.Equal(new[] { nameof(OutputInterfaceType.Ddp) }, Marked(vm.InterfaceOptionRows));
            Assert.Equal(new[] { "WS2812B" }, Marked(vm.PixelTypeOptionRows));

            vm.NewInterface = OutputInterfaceType.ArtNet;
            vm.NewPixelType = "Sheds";

            Assert.Equal(new[] { nameof(OutputInterfaceType.ArtNet) }, Marked(vm.InterfaceOptionRows));
            Assert.Equal(new[] { "Sheds" }, Marked(vm.PixelTypeOptionRows));
        }

        /// <summary>The row's whole text is one binding, so the marker has to notify with it.</summary>
        [Fact]
        public void ARowsDisplayText_CarriesItsOwnMarker()
        {
            var (vm, _) = NewEditor(PixelMappingTestData.Strip("A", 0f, 0f, pixelCount: 4));
            var artNet = RowFor(vm.InterfaceOptionRows, nameof(OutputInterfaceType.ArtNet));

            var notified = new List<string?>();
            artNet.PropertyChanged += (s, e) => notified.Add(e.PropertyName);

            vm.NewInterface = OutputInterfaceType.ArtNet;

            Assert.StartsWith("● ", artNet.Display);
            Assert.EndsWith(nameof(OutputInterfaceType.ArtNet), artNet.Display);
            Assert.Contains(nameof(OptionRow.Display), notified);
        }

        /// <summary>The registry resolves names case-insensitively, so a saved map may carry either.</summary>
        [Fact]
        public void APixelTypeInAnotherCasing_StillMarksItsRow()
        {
            var (vm, _) = NewEditor(PixelMappingTestData.Strip("A", 0f, 0f, pixelCount: 4));

            vm.NewPixelType = "ws2812b";

            Assert.Equal(new[] { "WS2812B" }, Marked(vm.PixelTypeOptionRows));
        }

        private static OptionRow RowFor(IReadOnlyList<OptionRow> rows, string label) =>
            rows.Single(row => row.Label == label);

        private static string[] Marked(IReadOnlyList<OptionRow> rows) =>
            rows.Where(row => row.IsSelected).Select(row => row.Label).ToArray();

        [Fact]
        public void TheZoomFollowsTheLoadedMapsImageSize()
        {
            var (vm, _) = NewEditor(PixelMappingTestData.Strip("A", 0f, 0f, pixelCount: 4));

            Assert.Equal(PixelMap.DefaultImageWidth, vm.Zoom.ImageWidth);
            Assert.Equal(PixelMap.DefaultImageHeight, vm.Zoom.ImageHeight);
            Assert.Equal(PreviewRegion.WholeImage(PixelMap.DefaultImageWidth, PixelMap.DefaultImageHeight), vm.Zoom.Region);
        }
    }
}
