using System;
using System.IO;
using ShowControl.PixelMapping;
using Xunit;

namespace ShowControl.Tests
{
    public class PixelMapPersistenceTests : IDisposable
    {
        private readonly string _directory;

        public PixelMapPersistenceTests()
        {
            _directory = Path.Combine(Path.GetTempPath(), "showcontrol-pixelmap-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_directory);
        }

        public void Dispose()
        {
            try
            {
                Directory.Delete(_directory, recursive: true);
            }
            catch (IOException)
            {
                // A leftover temp directory is not worth failing a test over.
            }
        }

        private string PathFor(string name) => Path.Combine(_directory, name);

        [Fact]
        public void WriteAtomic_CreatesTheDirectory_SoAFirstRunDoesNotHaveToPrepareIt()
        {
            string nested = Path.Combine(_directory, "a", "b", "map.json");

            PixelMapFile.WriteAtomic(nested, "{}");

            Assert.True(File.Exists(nested));
            Assert.Equal("{}", File.ReadAllText(nested));
        }

        [Fact]
        public void WriteAtomic_LeavesNoTemporaryFileBehind()
        {
            string path = PathFor("map.json");

            PixelMapFile.WriteAtomic(path, "first");
            PixelMapFile.WriteAtomic(path, "second");

            Assert.Equal("second", File.ReadAllText(path));
            Assert.False(File.Exists(path + ".tmp"));
        }

        /// <summary>
        /// The point of writing through a temporary file: a failed write must not be able to leave
        /// the user with a truncated map where a good one used to be.
        /// </summary>
        [Fact]
        public void WriteAtomic_LeavesThePreviousFileIntact_WhenTheWriteFails()
        {
            string path = PathFor("map.json");
            PixelMapFile.WriteAtomic(path, "good contents");

            // Holding the temporary path open as a directory makes the write throw part-way.
            Directory.CreateDirectory(path + ".tmp");

            Assert.ThrowsAny<Exception>(() => PixelMapFile.WriteAtomic(path, "replacement"));
            Assert.Equal("good contents", File.ReadAllText(path));
        }

        [Fact]
        public void TryRead_ReportsAMissingFile_RatherThanThrowing()
        {
            Assert.False(PixelMapFile.TryRead(PathFor("absent.json"), out string contents));
            Assert.Equal(string.Empty, contents);
            Assert.False(PixelMapFile.TryRead(null, out _));
        }

        [Fact]
        public void ComputeHash_ChangesWithContent_SoAHalfWrittenFileIsDetectable()
        {
            string a = PixelMapSerializer.ComputeHash("contents");
            string b = PixelMapSerializer.ComputeHash("contents ");

            Assert.NotEqual(a, b);
            Assert.Equal(a, PixelMapSerializer.ComputeHash("contents"));
        }

        [Fact]
        public void State_RoundTrips()
        {
            var state = new PixelMapState
            {
                SavedPath = "/somewhere/venue.json",
                SavedContentHash = "ABC",
                WorkingContentHash = "DEF",
                IsDirty = true,
                LastAutosaveUtc = DateTimeOffset.UnixEpoch
            };

            var restored = PixelMapSerializer.DeserializeState(PixelMapSerializer.SerializeState(state));

            Assert.Equal(state.SavedPath, restored.SavedPath);
            Assert.Equal(state.SavedContentHash, restored.SavedContentHash);
            Assert.Equal(state.WorkingContentHash, restored.WorkingContentHash);
            Assert.True(restored.IsDirty);
            Assert.Equal(state.LastAutosaveUtc, restored.LastAutosaveUtc);
        }

        [Fact]
        public void MovingAFixture_ClassifiesAsGeometry_SoADragNeverRebuildsTheRenderPipeline()
        {
            var before = DefaultPixelMap.Load();
            var after = before.Clone();
            after.Fixtures[0].OriginX += 25f;
            after.Fixtures[0].OriginY -= 4f;

            Assert.Equal(PixelMapChangeKind.Geometry, PixelMapSession.Classify(before, after));
        }

        [Fact]
        public void BendingAFixture_ClassifiesAsGeometry_BecauseOnlyOffsetsChange()
        {
            var before = DefaultPixelMap.Load();
            var after = before.Clone();
            PixelLayoutTools.RotateSection(after.Fixtures[0].PixelOffsets, fromIndex: 300, turnDegrees: 30f);

            Assert.Equal(PixelMapChangeKind.Geometry, PixelMapSession.Classify(before, after));
        }

        [Theory]
        [InlineData("enabled")]
        [InlineData("pixelcount")]
        [InlineData("pixeltype")]
        [InlineData("colororder")]
        [InlineData("startchannel")]
        [InlineData("ipaddress")]
        [InlineData("removed")]
        public void ChangingTheRigsShape_ClassifiesAsStructural(string change)
        {
            var before = DefaultPixelMap.Load();
            var after = before.Clone();
            var fixture = after.Fixtures[0];

            switch (change)
            {
                case "enabled": fixture.Enabled = !fixture.Enabled; break;
                case "pixelcount": fixture.PixelOffsets = PixelLayoutTools.GenerateLine(600); break;
                case "pixeltype": fixture.PixelType = "AsteraAX3"; break;
                case "colororder": fixture.ColorOrder = ColorOrder.Grb; break;
                case "startchannel": fixture.Output.StartChannel += 3; break;
                case "ipaddress": fixture.Output.IpAddress = "10.0.0.1"; break;
                case "removed": after.Fixtures.RemoveAt(0); break;
            }

            Assert.Equal(PixelMapChangeKind.Structural, PixelMapSession.Classify(before, after));
        }

        [Fact]
        public void ApplyingAnInvalidEdit_ReturnsTheProblems_AndLeavesTheLiveMapAlone()
        {
            var session = PixelMapSession.FromMap(DefaultPixelMap.Load(), new TestPixelTypes());
            var before = session.Current;

            var broken = before.Clone();
            broken.Fixtures[0].PixelType = "NotARealFixture";

            var problems = session.Apply(broken);

            Assert.NotEmpty(problems);
            Assert.Same(before, session.Current);
            Assert.False(session.IsDirty);
        }

        [Fact]
        public void ApplyingAValidEdit_PublishesNewGeometry_AndRaisesChangedOnce()
        {
            var session = PixelMapSession.FromMap(DefaultPixelMap.Load(), new TestPixelTypes());
            var beforeSampleMap = session.CurrentSampleMap;

            PixelMapChangedEventArgs? observed = null;
            session.Changed += (_, e) => observed = e;

            var moved = session.Current.Clone();
            moved.Fixtures[0].OriginY = 100f;

            Assert.Empty(session.Apply(moved));

            Assert.NotNull(observed);
            Assert.Equal(PixelMapChangeKind.Geometry, observed!.Kind);
            Assert.NotSame(beforeSampleMap, session.CurrentSampleMap);
            Assert.Equal(((100 * PixelMap.DefaultImageWidth) + 0) * 4, session.CurrentSampleMap.ByteOffsets[0]);
            Assert.True(session.IsDirty);
        }
    }
}
