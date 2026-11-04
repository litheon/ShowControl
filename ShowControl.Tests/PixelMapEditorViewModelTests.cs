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

        public PixelOutputOverrideRequest CurrentOutputOverride => _session.CurrentOutputOverride;

        public event EventHandler? OutputOverrideChanged
        {
            add => _session.OutputOverrideChanged += value;
            remove => _session.OutputOverrideChanged -= value;
        }

        public void SetOutputOverride(PixelOutputOverrideRequest request) => _session.SetOutputOverride(request);

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
        /// A press on the fixture that is already selected grabs the pixel under the cursor as the
        /// handle of a hinge at the pixel before it: the run from the handle onward swings to point
        /// at the cursor, the run before the hinge stays put. Same invariants as the whole-fixture
        /// drag — every move publishes, nothing the panel is bound to is touched until the drag ends.
        /// </summary>
        [Fact]
        public void PressingTheSelectedFixture_GrabsThePixelUnderTheCursor_AndBendsTheRunThere()
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
            Assert.Contains("bending at pixel 4", vm.StatusText);
            int appliesAfterBegin = editor.ApplyCount;

            // Straight below the hinge (pixel 4 at 104,100): the tail turns to run straight down.
            vm.DragTo(104d, 110d);

            Assert.Equal(appliesAfterBegin + 1, editor.ApplyCount);
            Assert.Equal(0, rowChanges);

            var fixture = vm.Working.Fixtures[0];
            var positions = PixelMapGeometry.Resolve(fixture);
            for (int i = 0; i <= 4; i++)
            {
                Assert.Equal(100f + i, positions[i].X);
                Assert.Equal(100f, positions[i].Y);
            }
            for (int i = 5; i < 10; i++)
            {
                Assert.Equal(104f, positions[i].X, 3);
                Assert.Equal(100f + (i - 4), positions[i].Y, 3);
            }

            // The origin did not move: the shape changed, not the fixture's anchor.
            Assert.Equal(100f, fixture.OriginX);
            Assert.Equal(100f, fixture.OriginY);

            Assert.True(vm.EndDrag());
            Assert.Null(vm.DraggedPixelIndex);
            Assert.Contains("bent at pixel 4", vm.StatusText);
            Assert.Contains("90°", vm.StatusText);
            Assert.Equal("90", vm.BendText);
        }

        /// <summary>
        /// Pixel 0 has no pixel before it, so grabbing it rotates the whole run about the origin —
        /// the ring that marks the first pixel doubles as the rotate handle. Pixel 0 itself never
        /// moves, so the origin still marks it without any rebase.
        /// </summary>
        [Fact]
        public void DraggingPixelZero_RotatesTheWholeRunAboutTheOrigin()
        {
            var (vm, _) = NewEditor(PixelMappingTestData.Strip("Strip", originX: 100f, originY: 100f, pixelCount: 6));

            vm.TryBeginDrag(100d, 100d, 5d); // select + whole drag
            vm.EndDrag();
            vm.TryBeginDrag(100d, 100d, 2d); // pixel 0 drag
            Assert.Equal(0, vm.DraggedPixelIndex);
            Assert.Contains("rotating", vm.StatusText);

            vm.DragTo(100d, 90d);
            vm.EndDrag();

            var fixture = vm.Working.Fixtures[0];
            Assert.Equal(100f, fixture.OriginX);
            Assert.Equal(100f, fixture.OriginY);
            Assert.Equal(0f, fixture.PixelOffsets[0]);
            Assert.Equal(0f, fixture.PixelOffsets[1]);
            var positions = PixelMapGeometry.Resolve(fixture);
            for (int i = 0; i < 6; i++)
            {
                Assert.Equal(100f, positions[i].X, 3);
                Assert.Equal(100f - i, positions[i].Y, 3);
            }
            Assert.Contains("rotated to 270°", vm.StatusText);
            Assert.Equal("0", vm.BendText);
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
        public void Straightening_UndoesAnyBend_AndPublishes()
        {
            var fixture = PixelMappingTestData.Strip("A", 50f, 50f, pixelCount: 8);
            PixelLayoutTools.RotateSection(fixture.PixelOffsets, fromIndex: 3, turnDegrees: 90f);
            var (vm, editor) = NewEditor(fixture);
            vm.SelectedFixtureId = fixture.Id;
            Assert.Equal("90", vm.BendText);
            int applies = editor.ApplyCount;

            vm.StraightenCommand.Execute(null);

            Assert.Equal(applies + 1, editor.ApplyCount);
            Assert.Equal(0f, PixelLayoutTools.MeasureBend(vm.Working.Fixtures[0].PixelOffsets), 3);
            Assert.Equal("0", vm.BendText);
            Assert.Equal(50f, vm.Working.Fixtures[0].OriginX);
            Assert.Contains("straight again", vm.StatusText);
        }

        [Fact]
        public void ApplyingABend_LaysTheRunOutAsAnArc_AndTheBoxReadsItBack()
        {
            var (vm, editor) = NewEditor(PixelMappingTestData.Strip("A", 50f, 50f, pixelCount: 20));
            vm.SelectedFixtureId = vm.Working.Fixtures[0].Id;
            int applies = editor.ApplyCount;

            vm.BendText = "90";
            vm.ApplyBendCommand.Execute(null);

            Assert.Equal(applies + 1, editor.ApplyCount);
            Assert.Equal(90f, PixelLayoutTools.MeasureBend(vm.Working.Fixtures[0].PixelOffsets), 2);
            Assert.Equal("90", vm.BendText);
            Assert.Equal(1f, PixelLayoutTools.MeasureSpacing(vm.Working.Fixtures[0].PixelOffsets), 3);
        }

        [Fact]
        public void ApplyingANonNumericBend_IsRefusedWithoutTouchingTheFixture()
        {
            var (vm, editor) = NewEditor(PixelMappingTestData.Strip("A", 0f, 0f, pixelCount: 5));
            vm.SelectedFixtureId = vm.Working.Fixtures[0].Id;
            var before = (float[])vm.Working.Fixtures[0].PixelOffsets.Clone();
            int applies = editor.ApplyCount;

            vm.BendText = "lots";
            vm.ApplyBendCommand.Execute(null);

            Assert.Equal(applies, editor.ApplyCount);
            Assert.Equal(before, vm.Working.Fixtures[0].PixelOffsets);
            Assert.Contains("Bend", vm.StatusText);
        }

        /// <summary>
        /// The list row is one truncating line; the readout is where every field is legible,
        /// including the channel range — which is what the operator reads off to wire the next strip.
        /// </summary>
        [Fact]
        public void SelectingAFixture_ShowsEveryFieldIncludingItsChannelRange()
        {
            var fixture = PixelMappingTestData.Strip("Stage left", 10f, 20f, pixelCount: 4, startChannel: 12);
            fixture.BlankedPixels.Add(new PixelRange(2, 2));
            var (vm, _) = NewEditor(fixture);
            Assert.Contains("Nothing selected", vm.SelectedFixtureDetail);

            vm.SelectedFixtureId = fixture.Id;

            var detail = vm.SelectedFixtureDetail;
            Assert.Contains("Stage left", detail);
            Assert.Contains("DDP 192.168.3.207 · port 4048", detail);
            Assert.Contains("WS2812B · 3 ch/px · 4 px · 12 ch", detail);
            Assert.Contains("Channels 12–23 (next free 24)", detail);
            Assert.Contains("Origin 10.0, 20.0 · spacing 1 · bend 0°", detail);
            Assert.Contains("Blanked 2-3 (2 px dark)", detail);

            vm.SelectedFixtureId = null;
            Assert.Contains("Nothing selected", vm.SelectedFixtureDetail);
        }

        [Fact]
        public void TheFixtureRow_ShowsTheChannelRangeAndTheController()
        {
            var fixture = PixelMappingTestData.Strip("A", 0f, 0f, pixelCount: 4, startChannel: 12);

            var row = FixtureRow.From(fixture, new TestPixelTypes());

            Assert.Equal("A · 4px · ch 12–23 · 192.168.3.207", row.Description);
        }

        /// <summary>
        /// The start channel used to be typed in by hand — the pixel counts of every strip already
        /// on the controller, added up and multiplied by the channels per pixel. The form does that
        /// sum, follows the controller fields while the box still holds its suggestion, and leaves
        /// a typed value alone.
        /// </summary>
        [Fact]
        public void TheAddForm_SuggestsTheNextFreeChannelOnTheChosenController_AndFollowsIt()
        {
            var a = PixelMappingTestData.Strip("A", 0f, 0f, pixelCount: 4, ipAddress: "192.168.3.207", startChannel: 0);
            var b = PixelMappingTestData.Strip("B", 0f, 50f, pixelCount: 10, ipAddress: "192.168.3.208", startChannel: 100);
            var (vm, _) = NewEditor(a, b);

            Assert.Equal("192.168.3.207", vm.NewIpAddress);
            Assert.Equal("12", vm.NewStartChannel);
            Assert.Contains("after 'A'", vm.StartChannelHint);

            vm.NewIpAddress = "192.168.3.208";
            Assert.Equal("130", vm.NewStartChannel);
            Assert.Contains("after 'B'", vm.StartChannelHint);

            vm.NewIpAddress = "192.168.3.209";
            Assert.Equal("0", vm.NewStartChannel);
            Assert.Contains("Nothing on", vm.StartChannelHint);

            // A typed value is the operator's: the hint follows, the box does not.
            vm.NewStartChannel = "500";
            vm.NewIpAddress = "192.168.3.207";
            Assert.Equal("500", vm.NewStartChannel);
            Assert.Contains("is 12", vm.StartChannelHint);

            vm.UseNextFreeChannelCommand.Execute(null);
            Assert.Equal("12", vm.NewStartChannel);
        }

        [Fact]
        public void AddingAFixture_TakesTheSuggestedChannel_AndAdvancesThePastIt()
        {
            var (vm, _) = NewEditor(PixelMappingTestData.Strip("A", 0f, 0f, pixelCount: 4, startChannel: 0));
            Assert.Equal("12", vm.NewStartChannel);

            vm.NewName = "B";
            vm.NewPixelCount = "10";
            vm.AddFixtureCommand.Execute(null);

            Assert.Equal(12, vm.Working.Fixtures[1].Output.StartChannel);
            Assert.Contains("channels 12–41", vm.StatusText);
            Assert.Equal("42", vm.NewStartChannel);
            Assert.Contains("after 'B'", vm.StartChannelHint);
            Assert.Contains("ch 12–41", vm.Rows[1].Description);
        }

        /// <summary>
        /// A disabled strip is still wired: its channels stay reserved so re-enabling it later does
        /// not collide with whatever was added in the meantime.
        /// </summary>
        [Fact]
        public void TheSuggestedChannel_CountsDisabledFixturesToo()
        {
            var (vm, _) = NewEditor(PixelMappingTestData.Strip("A", 0f, 0f, pixelCount: 4, startChannel: 0, enabled: false));

            Assert.Equal("12", vm.NewStartChannel);
        }

        [Fact]
        public void CommandsWithNothingSelected_SayWhatIsMissingRatherThanThrowing()
        {
            var (vm, _) = NewEditor(PixelMappingTestData.Strip("A", 0f, 0f, pixelCount: 4));

            foreach (var command in new[] { vm.ToggleEnabledCommand, vm.RemoveFixtureCommand, vm.FlipDirectionCommand, vm.ApplySpacingCommand, vm.StraightenCommand, vm.ApplyBendCommand })
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

        // ------------------------------------------------------------------ blanked pixels

        [Fact]
        public void ApplyingBlankedPixels_PublishesThem_AndTheSampleMapGoesDarkThere()
        {
            var fixture = PixelMappingTestData.Strip("A", 0f, 0f, pixelCount: 8);
            var (vm, editor) = NewEditor(fixture);
            vm.SelectedFixtureId = fixture.Id;
            Assert.Equal(string.Empty, vm.BlankedText);
            int appliesBefore = editor.ApplyCount;

            vm.BlankedText = "6-7, 2";
            vm.ApplyBlankedCommand.Execute(null);

            Assert.Equal(appliesBefore + 1, editor.ApplyCount);
            Assert.Equal(new List<PixelRange> { new(2, 1), new(6, 2) }, vm.Working.Fixtures[0].BlankedPixels);
            Assert.Equal("2, 6-7", vm.BlankedText);                      // read back canonical
            Assert.Contains("Blanked 2, 6-7 (3 px dark)", vm.SelectedFixtureDetail);
            var offsets = editor.CurrentSampleMap.ByteOffsets.ToArray();
            Assert.Equal(PixelSampleMap.Blank, offsets[2]);
            Assert.Equal(PixelSampleMap.Blank, offsets[6]);
            Assert.Equal(PixelSampleMap.Blank, offsets[7]);
            Assert.NotEqual(PixelSampleMap.Blank, offsets[5]);

            // Clearing the box un-blanks them.
            vm.BlankedText = "";
            vm.ApplyBlankedCommand.Execute(null);
            Assert.Empty(vm.Working.Fixtures[0].BlankedPixels);
            Assert.DoesNotContain(PixelSampleMap.Blank, editor.CurrentSampleMap.ByteOffsets.ToArray());
        }

        [Theory]
        [InlineData("8")]
        [InlineData("3-12")]
        [InlineData("tail")]
        public void ApplyingBlankedPixelsOutsideTheFixture_IsRefusedWithoutTouchingIt(string text)
        {
            var fixture = PixelMappingTestData.Strip("A", 0f, 0f, pixelCount: 8);
            var (vm, editor) = NewEditor(fixture);
            vm.SelectedFixtureId = fixture.Id;
            int appliesBefore = editor.ApplyCount;

            vm.BlankedText = text;
            vm.ApplyBlankedCommand.Execute(null);

            Assert.Equal(appliesBefore, editor.ApplyCount);
            Assert.Empty(vm.Working.Fixtures[0].BlankedPixels);
            Assert.Contains(text, vm.StatusText);
        }

        // ------------------------------------------------------------------ editing a fixture

        [Fact]
        public void LoadingTheSelectedFixture_FillsTheForm()
        {
            var fixture = PixelMappingTestData.Strip("Stage left", 10f, 20f, pixelCount: 6, ipAddress: "10.0.0.5", startChannel: 300);
            fixture.PixelType = "Sheds";
            fixture.PixelOffsets = PixelLayoutTools.GenerateLine(6, spacing: 2f, angleDegrees: 90f);
            var (vm, _) = NewEditor(fixture);
            vm.SelectedFixtureId = fixture.Id;

            vm.LoadSelectedIntoFormCommand.Execute(null);

            Assert.Equal("Stage left", vm.NewName);
            Assert.Equal("10.0.0.5", vm.NewIpAddress);
            Assert.Equal("300", vm.NewStartChannel);
            Assert.Equal("Sheds", vm.NewPixelType);
            Assert.Equal("6", vm.NewPixelCount);
            Assert.Equal("2", vm.NewSpacing);
            Assert.Equal("90", vm.NewAngle);
            Assert.Equal(OutputInterfaceType.Ddp, vm.NewInterface);
        }

        [Fact]
        public void ApplyingTheFormToTheSelectedFixture_RewritesItInPlace_AndExtendsTheRun()
        {
            var fixture = PixelMappingTestData.Strip("A", 10f, 20f, pixelCount: 4, startChannel: 0);
            fixture.BlankedPixels.Add(new PixelRange(3, 1));
            var other = PixelMappingTestData.Strip("B", 50f, 20f, pixelCount: 4, startChannel: 12);
            var (vm, editor) = NewEditor(fixture, other);
            vm.SelectedFixtureId = fixture.Id;
            vm.LoadSelectedIntoFormCommand.Execute(null);
            int appliesBefore = editor.ApplyCount;

            vm.NewName = "A renamed";
            vm.NewStartChannel = "24";
            vm.NewPixelCount = "6";
            vm.ApplyFormToSelectedCommand.Execute(null);

            Assert.Equal(appliesBefore + 1, editor.ApplyCount);
            Assert.Equal(2, vm.Working.Fixtures.Count);                 // edited, not added
            var updated = vm.Working.Fixtures[0];
            Assert.Equal(fixture.Id, updated.Id);
            Assert.Equal("A renamed", updated.Name);
            Assert.Equal(24, updated.Output.StartChannel);
            Assert.Equal(6, updated.PixelCount);
            Assert.Equal(10f, updated.OriginX);                          // it did not move
            Assert.Equal(new[] { 4f, 0f, 5f, 0f }, updated.PixelOffsets[8..]);   // carried on at its spacing
            Assert.Equal(new List<PixelRange> { new(3, 1) }, updated.BlankedPixels);
            Assert.Contains("A renamed", vm.Rows[0].Description);
            Assert.Contains("extended from 4 to 6 px", vm.StatusText);

            // Cutting it short drops the blanked pixels beyond the end.
            vm.NewPixelCount = "3";
            vm.ApplyFormToSelectedCommand.Execute(null);
            Assert.Equal(3, vm.Working.Fixtures[0].PixelCount);
            Assert.Empty(vm.Working.Fixtures[0].BlankedPixels);
        }

        [Fact]
        public void ARejectedFormApply_PutsTheFixtureBack()
        {
            var fixture = PixelMappingTestData.Strip("A", 10f, 20f, pixelCount: 4);
            var (vm, editor) = NewEditor(fixture);
            vm.SelectedFixtureId = fixture.Id;
            vm.LoadSelectedIntoFormCommand.Execute(null);
            editor.RejectEdits = true;

            vm.NewName = "Nope";
            vm.NewPixelCount = "9";
            vm.ApplyFormToSelectedCommand.Execute(null);

            Assert.Equal("A", vm.Working.Fixtures[0].Name);
            Assert.Equal(4, vm.Working.Fixtures[0].PixelCount);
            Assert.Contains("rejected", vm.StatusText);
        }

        // ------------------------------------------------------------------ wiring checks

        [Fact]
        public void TestPatternsAndSolo_ReachTheSession_AndClearOnLeavingEditMode()
        {
            var a = PixelMappingTestData.Strip("A", 0f, 0f);
            var b = PixelMappingTestData.Strip("B", 50f, 0f, startChannel: 12);
            var (vm, editor) = NewEditor(a, b);
            vm.IsEditing = true;
            Assert.Equal("Showing the animation.", vm.OutputOverrideText);

            vm.SetTestPatternCommand.Execute(PixelTestPattern.Rainbow);
            Assert.Equal(PixelTestPattern.Rainbow, editor.CurrentOutputOverride.Pattern);
            Assert.Contains("rainbow", vm.OutputOverrideText);

            vm.SelectedFixtureId = b.Id;
            vm.SoloSelectedCommand.Execute(null);
            Assert.Equal(b.Id, editor.CurrentOutputOverride.SoloFixtureId);
            Assert.Equal(PixelTestPattern.Rainbow, editor.CurrentOutputOverride.Pattern);   // the pattern stays
            Assert.Contains("Solo 'B'", vm.OutputOverrideText);

            vm.SoloSelectedCommand.Execute(null);                                            // again: un-solo
            Assert.Null(editor.CurrentOutputOverride.SoloFixtureId);

            vm.SoloSelectedCommand.Execute(null);
            vm.SetTestPatternCommand.Execute(PixelTestPattern.None);
            Assert.Equal(new PixelOutputOverrideRequest(PixelTestPattern.None, b.Id), editor.CurrentOutputOverride);

            vm.IsEditing = false;
            Assert.Equal(PixelOutputOverrideRequest.Nothing, editor.CurrentOutputOverride);
            Assert.Equal("Showing the animation.", vm.OutputOverrideText);
        }

        [Fact]
        public void ShowAnimation_ClearsBothPatternAndSolo()
        {
            var a = PixelMappingTestData.Strip("A", 0f, 0f);
            var (vm, editor) = NewEditor(a);
            vm.SelectedFixtureId = a.Id;
            vm.SetTestPatternCommand.Execute(PixelTestPattern.Chase);
            vm.SoloSelectedCommand.Execute(null);
            Assert.True(editor.CurrentOutputOverride.IsActive);

            vm.ClearOutputOverrideCommand.Execute(null);
            Assert.Equal(PixelOutputOverrideRequest.Nothing, editor.CurrentOutputOverride);
        }

        [Fact]
        public void SoloWithNothingSelected_SaysSo_AndChangesNothing()
        {
            var (vm, editor) = NewEditor(PixelMappingTestData.Strip("A", 0f, 0f));
            vm.SoloSelectedCommand.Execute(null);
            Assert.Equal(PixelOutputOverrideRequest.Nothing, editor.CurrentOutputOverride);
            Assert.Contains("Select a fixture", vm.StatusText);
        }

        [Fact]
        public void ApplyingTheFormWithNothingSelected_SaysSo()
        {
            var (vm, editor) = NewEditor(PixelMappingTestData.Strip("A", 0f, 0f));
            int appliesBefore = editor.ApplyCount;
            vm.ApplyFormToSelectedCommand.Execute(null);
            vm.LoadSelectedIntoFormCommand.Execute(null);
            vm.ApplyBlankedCommand.Execute(null);
            Assert.Equal(appliesBefore, editor.ApplyCount);
            Assert.Contains("Select a fixture", vm.StatusText);
        }
    }
}
