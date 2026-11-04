using System;

namespace ShowControl.PixelMapping
{
    /// <summary>
    /// The channel arithmetic the editor does so the operator does not have to.
    /// <para>
    /// A fixture's start channel is explicit in the file (see <see cref="OutputTarget.StartChannel"/>)
    /// and used to be typed in by hand, which meant adding up the pixel counts of every strip already
    /// on the controller and multiplying by the channels per pixel. This is that sum.
    /// </para>
    /// </summary>
    public static class ChannelPlanner
    {
        /// <summary>
        /// The first channel after everything already on <paramref name="target"/>: the largest
        /// end channel of any fixture there, or zero if nothing is.
        /// <para>
        /// Disabled fixtures count. A venue toggle takes a strip out of the layout, not off the
        /// wire — its channels are still physically spoken for on that controller, and a new strip
        /// addressed on top of them would collide the day the old one is enabled again. A pixel type
        /// the resolver does not know contributes no channels; validation will refuse the map anyway.
        /// </para>
        /// </summary>
        /// <param name="after">The fixture the returned channel follows, or null if the target is empty.</param>
        public static int NextFreeChannel(PixelMap map, OutputTargetKey target, IPixelTypeResolver pixelTypes, out FixtureDefinition? after)
        {
            if (map == null) throw new ArgumentNullException(nameof(map));
            if (pixelTypes == null) throw new ArgumentNullException(nameof(pixelTypes));

            int next = 0;
            after = null;
            foreach (var fixture in map.Fixtures)
            {
                if (fixture.Output.Key != target)
                {
                    continue;
                }

                int end = fixture.Output.StartChannel + ChannelCount(fixture, pixelTypes);
                if (after == null || end > next)
                {
                    next = end;
                    after = fixture;
                }
            }

            return next;
        }

        /// <summary>Channels the fixture occupies on the wire; zero if its pixel type is unknown.</summary>
        public static int ChannelCount(FixtureDefinition fixture, IPixelTypeResolver pixelTypes)
        {
            if (fixture == null) throw new ArgumentNullException(nameof(fixture));
            if (pixelTypes == null) throw new ArgumentNullException(nameof(pixelTypes));

            return pixelTypes.TryGetChannelCount(fixture.PixelType, out int channelsPerPixel)
                ? fixture.PixelCount * channelsPerPixel
                : 0;
        }
    }
}
