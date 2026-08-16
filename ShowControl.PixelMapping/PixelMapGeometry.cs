using System;
using System.Drawing;

namespace ShowControl.PixelMapping
{
    /// <summary>
    /// Resolves a fixture's stored offsets into absolute shader-image positions.
    /// <para>
    /// Trivial by design — the model stores positions, not a recipe for them. It stays a named
    /// function so the sample-map builder and the editor's drawable go through one place and
    /// cannot drift apart.
    /// </para>
    /// </summary>
    public static class PixelMapGeometry
    {
        public static int Resolve(FixtureDefinition fixture, Span<PointF> destination)
        {
            if (fixture == null) throw new ArgumentNullException(nameof(fixture));

            int count = fixture.PixelCount;
            if (destination.Length < count)
            {
                throw new ArgumentException(
                    $"Destination holds {destination.Length} points but the fixture has {count} pixels.",
                    nameof(destination));
            }

            float[] offsets = fixture.PixelOffsets;
            float originX = fixture.OriginX;
            float originY = fixture.OriginY;
            for (int i = 0; i < count; i++)
            {
                destination[i] = new PointF(originX + offsets[i * 2], originY + offsets[(i * 2) + 1]);
            }
            return count;
        }

        public static PointF Resolve(FixtureDefinition fixture, int pixelIndex)
        {
            if (fixture == null) throw new ArgumentNullException(nameof(fixture));
            if (pixelIndex < 0 || pixelIndex >= fixture.PixelCount)
            {
                throw new ArgumentOutOfRangeException(nameof(pixelIndex));
            }

            return new PointF(
                fixture.OriginX + fixture.PixelOffsets[pixelIndex * 2],
                fixture.OriginY + fixture.PixelOffsets[(pixelIndex * 2) + 1]);
        }

        /// <summary>Allocating convenience for the editor, which redraws far below frame rate.</summary>
        public static PointF[] Resolve(FixtureDefinition fixture)
        {
            if (fixture == null) throw new ArgumentNullException(nameof(fixture));
            var points = new PointF[fixture.PixelCount];
            Resolve(fixture, points);
            return points;
        }
    }
}
