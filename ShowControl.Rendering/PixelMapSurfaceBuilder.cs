using System;
using System.Collections.Generic;
using ShowControl.PixelMapping;

namespace ShowControl.Rendering
{
    /// <summary>
    /// Turns a compiled layout into the render surface tree.
    /// <para>
    /// Surfaces are emitted in slot order, which is the same order the layout assigns pixel and
    /// channel offsets in, so the existing sequential walk in <see cref="RenderSurface"/> lines up
    /// with the layout by construction rather than by convention.
    /// </para>
    /// </summary>
    public static class PixelMapSurfaceBuilder
    {
        public static IRenderSurface Build(PixelMapLayout layout, PixelTypeRegistry pixelTypes, RenderSettings renderSettings)
        {
            if (layout == null) throw new ArgumentNullException(nameof(layout));
            if (pixelTypes == null) throw new ArgumentNullException(nameof(pixelTypes));

            var surfaces = new List<IRenderSurface>(layout.Slots.Count);
            foreach (var slot in layout.Slots)
            {
                if (slot.Kind == LayoutSlotKind.Gap)
                {
                    surfaces.Add(new GapRenderSurface(slot.ChannelCount));
                    continue;
                }

                var fixture = slot.Fixture!;
                surfaces.Add(new PixelRunRenderSurface(
                    pixelTypes.Create(fixture.PixelType),
                    fixture.PixelCount,
                    fixture.ColorOrder));
            }

            var surface = RenderSurface.Create(renderSettings, surfaces.ToArray());

            if (surface.TotalPixelCount != layout.TotalPixelCount || surface.TotalChannelCount != layout.TotalChannelCount)
            {
                throw new InvalidOperationException(
                    $"Surface tree ({surface.TotalPixelCount}px / {surface.TotalChannelCount}ch) does not match the layout " +
                    $"({layout.TotalPixelCount}px / {layout.TotalChannelCount}ch).");
            }

            return surface;
        }
    }
}
