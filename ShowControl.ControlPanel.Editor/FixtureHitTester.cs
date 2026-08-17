using System;
using ShowControl.PixelMapping;

namespace ShowControl.ControlPanel.Editor
{
    /// <summary>
    /// Finds the fixture under a point in image space.
    /// </summary>
    public static class FixtureHitTester
    {
        /// <summary>
        /// Nearest pixel within <paramref name="threshold"/> image units wins, so overlapping strips
        /// resolve to the one actually under the cursor rather than the first in the file.
        /// <para>
        /// The threshold comes from a fixed radius in overlay points converted through the current
        /// zoom, which is what keeps the grab radius constant on screen while getting finer in image
        /// units as the operator zooms in.
        /// </para>
        /// </summary>
        public static FixtureDefinition? HitTest(PixelMap map, double imageX, double imageY, double threshold)
        {
            if (map == null) throw new ArgumentNullException(nameof(map));

            FixtureDefinition? best = null;
            double bestDistanceSquared = threshold * threshold;

            foreach (var fixture in map.Fixtures)
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

        /// <summary>
        /// How many of a fixture's pixels fall outside the image. Off-image pixels sample black
        /// rather than clamping, so a fixture pushed past the edge goes dark from that point on and
        /// the editor says so instead of leaving it to be discovered.
        /// </summary>
        public static int CountPixelsOutsideImage(FixtureDefinition fixture, int imageWidth, int imageHeight)
        {
            if (fixture == null) throw new ArgumentNullException(nameof(fixture));

            int outside = 0;
            for (int i = 0; i < fixture.PixelCount; i++)
            {
                var position = PixelMapGeometry.Resolve(fixture, i);
                if (position.X < 0f || position.X >= imageWidth ||
                    position.Y < 0f || position.Y >= imageHeight)
                {
                    outside++;
                }
            }
            return outside;
        }
    }
}
