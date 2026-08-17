using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
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

        public PixelMap Current => _session.Current;

        public PixelSampleMap CurrentSampleMap => _session.CurrentSampleMap;

        public PixelMapLayout CurrentLayout => _session.CurrentLayout;

        public PixelMapSummary CurrentSummary => _session.CurrentSummary;

        public bool IsDirty => _session.IsDirty;

        public string? SavedPath => _session.SavedPath;

        public bool RecoveredUnsavedWork => _session.RecoveredUnsavedWork;

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

        public void Save(string path) => throw new NotSupportedException();

        public PixelMap Open(string path) => throw new NotSupportedException();

        public void KeepRecoveredWork() => _session.KeepRecoveredWork();

        public void DiscardRecoveredWork() => _session.DiscardRecoveredWork();
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
