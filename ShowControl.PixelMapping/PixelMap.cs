using System.Collections.Generic;

namespace ShowControl.PixelMapping
{
    /// <summary>
    /// The saved rig layout: which fixtures exist, where each pixel sits in shader-image space,
    /// and where its channels are sent.
    /// </summary>
    public sealed class PixelMap
    {
        public const int CurrentVersion = 1;

        /// <summary>Shader render target width. Fixed by <c>GraphicsHost.RunHeadless</c>.</summary>
        public const int DefaultImageWidth = 1080;

        /// <summary>Shader render target height. Fixed by <c>GraphicsHost.RunHeadless</c>.</summary>
        public const int DefaultImageHeight = 960;

        public int Version { get; set; } = CurrentVersion;

        public string? Name { get; set; }

        /// <summary>
        /// Coordinate space the pixel positions are expressed in. The offscreen render target never
        /// resizes — window resize only changes where that fixed image is stretched to — so this
        /// stays constant for the life of the process.
        /// </summary>
        public int ImageWidth { get; set; } = DefaultImageWidth;

        public int ImageHeight { get; set; } = DefaultImageHeight;

        public List<FixtureDefinition> Fixtures { get; set; } = new List<FixtureDefinition>();

        public PixelMap Clone()
        {
            var clone = new PixelMap
            {
                Version = Version,
                Name = Name,
                ImageWidth = ImageWidth,
                ImageHeight = ImageHeight,
                Fixtures = new List<FixtureDefinition>(Fixtures.Count)
            };
            for (int i = 0; i < Fixtures.Count; i++)
            {
                clone.Fixtures.Add(Fixtures[i].Clone());
            }
            return clone;
        }
    }
}
