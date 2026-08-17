using System;
using System.Collections.Generic;
using System.Linq;
using ShowControl.PixelMapping;

namespace ShowControl.Tests
{
    /// <summary>
    /// Stands in for the real registry, which lives in ShowControl.Rendering and pulls in the DMX
    /// fixture catalogue. Hand-written rather than mocked, matching the rest of the suite.
    /// </summary>
    internal sealed class TestPixelTypes : IPixelTypeResolver
    {
        private readonly Dictionary<string, int> _channelCounts = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["WS2812B"] = 3,
            ["Sheds"] = 10,
            ["AsteraAX3"] = 4
        };

        public bool TryGetChannelCount(string pixelType, out int channelCount) =>
            _channelCounts.TryGetValue(pixelType ?? string.Empty, out channelCount);

        public IReadOnlyList<string> KnownPixelTypes => _channelCounts.Keys.ToArray();
    }

    internal static class PixelMappingTestData
    {
        public const int ImageWidth = PixelMap.DefaultImageWidth;
        public const int ImageHeight = PixelMap.DefaultImageHeight;
        public const int PixelsPerStrip = 720;
        public const int StripCount = 16;
        public const int LegacyPixelCount = PixelsPerStrip * StripCount;

        /// <summary>
        /// The render target the legacy sampling loop was written against, and the factor the shader
        /// image was enlarged by since. The loop's constants are that image's geometry, so the
        /// parity test runs at these dimensions rather than the current ones.
        /// </summary>
        public const int LegacyImageWidth = 1080;

        public const int LegacyImageHeight = 960;

        public const float LegacyScale = (float)ImageWidth / LegacyImageWidth;

        /// <summary>
        /// A synthetic RGBA image whose bytes are a deterministic function of (x, y), so a sampling
        /// offset that is wrong by a row, a column or a channel cannot coincidentally match.
        /// </summary>
        public static byte[] SyntheticImage() => SyntheticImage(ImageWidth, ImageHeight);

        public static byte[] SyntheticImage(int width, int height)
        {
            var image = new byte[width * height * 4];
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int at = ((y * width) + x) * 4;
                    image[at] = (byte)(x & 0xFF);
                    image[at + 1] = (byte)(y & 0xFF);
                    image[at + 2] = (byte)(((x * 7) + (y * 13)) & 0xFF);
                    image[at + 3] = 0xFF;
                }
            }
            return image;
        }

        /// <summary>
        /// The sampling loop exactly as it stood in ShaderAnimation.AnimateFrame before the pixel
        /// map replaced it, magic literals and all. Kept verbatim: it is the reference the new
        /// coordinate-driven path has to reproduce byte for byte.
        /// </summary>
        public static RgbSample[] LegacySample(byte[] span, int width, int height, int pixelCount)
        {
            var frame = new RgbSample[pixelCount];
            var initial = ((height / 2) * width * 4) - (width * 80 * 4);
            int offset = 0;
            int pixelStride = 0;
            for (int i = 0; i < pixelCount; i++)
            {
                var rowOffset = offset * width * 4;
                var r = initial + pixelStride + rowOffset;
                var g = initial + pixelStride + rowOffset + 1;
                var b = initial + pixelStride + rowOffset + 2;
                int posInStrip = i % 720;
                if (posInStrip >= 708) // last 12 pixels of each strip
                {
                    frame[i] = default;
                }
                else
                {
                    frame[i] = new RgbSample(span[r], span[g], span[b]);
                }
                if (i == 719 || i == 1439 || i == 2159 || i == 2879 || i == 3599 || i == 4319 || i == 5039 ||
                    i == 5759 || i == 6479 || i == 7199 || i == 7919 || i == 8639 || i == 9359 || i == 10079 ||
                    i == 10799)
                {
                    offset += 10;
                    pixelStride = 0;
                }
                else
                {
                    pixelStride += 4;
                }
            }
            return frame;
        }

        /// <summary>The shipped default map with every strip enabled, for whole-rig comparisons.</summary>
        public static PixelMap AllStripsEnabled()
        {
            var map = DefaultPixelMap.Load();
            foreach (var fixture in map.Fixtures)
            {
                fixture.Enabled = true;
            }
            return map;
        }

        /// <summary>
        /// The venue rig as it stood on the 1080x960 render target: 16 strips of 720 on image rows
        /// 400+10k, one pixel per image column, the last twelve of each strip dead, four strips live
        /// on the one controller that was actually wired.
        /// <para>
        /// Written out here rather than derived from the shipped map, because it is the fixed point
        /// two separate tests are pinned to: that the coordinate-driven sampler reproduces the
        /// legacy loop on the geometry that loop was written for, and that the shipped map is this
        /// same rig scaled to the current image.
        /// </para>
        /// </summary>
        public static PixelMap LegacyRig()
        {
            string[] controllers = { "192.168.3.207", "192.168.3.208", "192.168.3.93", "192.168.3.103" };

            var map = new PixelMap
            {
                ImageWidth = LegacyImageWidth,
                ImageHeight = LegacyImageHeight
            };

            for (int strip = 0; strip < StripCount; strip++)
            {
                var fixture = new FixtureDefinition
                {
                    Name = $"Strip {strip + 1}",
                    Enabled = strip < 4,
                    Output = new OutputTarget
                    {
                        Interface = OutputInterfaceType.Ddp,
                        IpAddress = controllers[strip / 4],
                        Port = OutputTarget.DefaultDdpPort,
                        StartChannel = (strip % 4) * PixelsPerStrip * 3
                    },
                    PixelType = "WS2812B",
                    OriginX = 0f,
                    OriginY = 400f + (10f * strip),
                    PixelOffsets = PixelLayoutTools.GenerateLine(PixelsPerStrip)
                };
                fixture.BlankedPixels.Add(new PixelRange(708, 12));
                map.Fixtures.Add(fixture);
            }

            return map;
        }

        /// <summary>The legacy rig with every strip enabled, matching the whole-frame legacy loop.</summary>
        public static PixelMap LegacyRigWithAllStripsEnabled()
        {
            var map = LegacyRig();
            foreach (var fixture in map.Fixtures)
            {
                fixture.Enabled = true;
            }
            return map;
        }

        public static FixtureDefinition Strip(
            string name,
            float originX,
            float originY,
            int pixelCount = 4,
            string ipAddress = "192.168.3.207",
            int startChannel = 0,
            bool enabled = true)
        {
            return new FixtureDefinition
            {
                Id = Guid.NewGuid(),
                Name = name,
                Enabled = enabled,
                Output = new OutputTarget { IpAddress = ipAddress, StartChannel = startChannel },
                OriginX = originX,
                OriginY = originY,
                PixelOffsets = PixelLayoutTools.GenerateLine(pixelCount)
            };
        }

        public static PixelMap MapOf(params FixtureDefinition[] fixtures)
        {
            var map = new PixelMap();
            map.Fixtures.AddRange(fixtures);
            return map;
        }
    }
}
