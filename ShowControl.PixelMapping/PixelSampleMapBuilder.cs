using System;
using System.Drawing;

namespace ShowControl.PixelMapping
{
    public static class PixelSampleMapBuilder
    {
        /// <summary>
        /// Compiles the sample map for a layout. Driven by the layout rather than the raw map so
        /// the sample order and the render order agree by construction.
        /// </summary>
        public static PixelSampleMap Build(PixelMap map, PixelMapLayout layout)
        {
            if (map == null) throw new ArgumentNullException(nameof(map));
            if (layout == null) throw new ArgumentNullException(nameof(layout));

            int width = map.ImageWidth;
            int height = map.ImageHeight;
            var offsets = new int[layout.TotalPixelCount];

            Span<PointF> points = default;
            PointF[]? rented = null;

            foreach (var slot in layout.Slots)
            {
                if (slot.Kind != LayoutSlotKind.Fixture || slot.Fixture == null)
                {
                    continue;
                }

                var fixture = slot.Fixture;
                if (rented == null || rented.Length < fixture.PixelCount)
                {
                    rented = new PointF[fixture.PixelCount];
                }
                points = rented.AsSpan(0, fixture.PixelCount);
                PixelMapGeometry.Resolve(fixture, points);

                for (int i = 0; i < fixture.PixelCount; i++)
                {
                    offsets[slot.PixelStart + i] = fixture.IsBlanked(i)
                        ? PixelSampleMap.Blank
                        : ToByteOffset(points[i], width, height);
                }
            }

            return new PixelSampleMap(width, height, offsets);
        }

        /// <summary>
        /// Floor rather than a cast: they differ for negatives, and dragging a fixture off the left
        /// or top edge produces negatives. Anything outside the image is blank, not clamped —
        /// clamping would smear the edge colour down the whole tail of a strip.
        /// </summary>
        private static int ToByteOffset(PointF point, int width, int height)
        {
            int x = (int)MathF.Floor(point.X);
            int y = (int)MathF.Floor(point.Y);

            if (x < 0 || x >= width || y < 0 || y >= height)
            {
                return PixelSampleMap.Blank;
            }

            return ((y * width) + x) * 4;
        }
    }
}
