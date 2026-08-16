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
        /// A synthetic RGBA image whose bytes are a deterministic function of (x, y), so a sampling
        /// offset that is wrong by a row, a column or a channel cannot coincidentally match.
        /// </summary>
        public static byte[] SyntheticImage()
        {
            var image = new byte[ImageWidth * ImageHeight * 4];
            for (int y = 0; y < ImageHeight; y++)
            {
                for (int x = 0; x < ImageWidth; x++)
                {
                    int at = ((y * ImageWidth) + x) * 4;
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
