using System;
using System.Collections.Generic;

namespace ShowControl.PixelMapping
{
    /// <summary>A pattern the rig shows instead of the animation, for checking the wiring.</summary>
    public enum PixelTestPattern
    {
        /// <summary>The animation, untouched.</summary>
        None = 0,

        /// <summary>
        /// A bright head running from pixel 0 to the end of every fixture, with a short trail: shows
        /// each strip's direction, its length, and any dead pixel the head skips.
        /// </summary>
        Chase,

        /// <summary>Hue by position along each fixture, red at pixel 0: shows order and any break.</summary>
        Rainbow,

        /// <summary>One solid colour per fixture, in list order: shows which strip is which.</summary>
        FixtureColors,

        /// <summary>Full white everywhere: power and colour order.</summary>
        White,

        /// <summary>
        /// Thin lines of distinct colours running down the image at 45°, drawn in image
        /// space rather than along the wire, so fixtures that are offset from one another
        /// show the same line where the map says they overlap: a strip whose map position
        /// is off breaks the line. Line the breaks up and the map matches the rig.
        /// </summary>
        Diagonals
    }

    /// <summary>
    /// What the operator has asked the output to show instead of the animation: a test pattern,
    /// a single fixture soloed, or both. A value, so it can be published with one volatile write.
    /// </summary>
    public readonly record struct PixelOutputOverrideRequest(PixelTestPattern Pattern, Guid? SoloFixtureId)
    {
        public static readonly PixelOutputOverrideRequest Nothing = new(PixelTestPattern.None, null);

        public bool IsActive => Pattern != PixelTestPattern.None || SoloFixtureId.HasValue;
    }

    /// <summary>
    /// A <see cref="PixelOutputOverrideRequest"/> compiled against a layout: for every pixel in frame
    /// order, which fixture it belongs to and where along it, so the per-frame question — what
    /// colour, if any, replaces the animation's for this pixel — is a couple of array reads.
    /// Immutable; recompile when the layout changes, since the frame order does.
    /// </summary>
    public sealed class PixelOutputOverride
    {
        private const double ChasePixelsPerSecond = 40.0;
        private const int ChaseTrail = 8;
        private const int ChaseRest = 12;              // dark pixels before the head comes round again
        /// <summary>Image units between one diagonal line and the next, measured along x.</summary>
        public const float DiagonalPeriod = 50f;
        /// <summary>How wide each line is, in image units along x (4 pixels at the shipped 1.25 spacing).</summary>
        public const float DiagonalWidth = 5f;
        private static readonly RgbSample Black = new(0, 0, 0);
        private static readonly RgbSample Background = new(6, 6, 6);   // between the chase heads and the lines: alive, not lit
        private static readonly RgbSample[] DiagonalColors =
        {
            new(255, 0, 0), new(0, 255, 0), new(0, 0, 255), new(255, 255, 0), new(0, 255, 255), new(255, 0, 255), new(255, 255, 255)
        };

        private readonly int[] _fixtureOrdinal;        // -1 for a pixel in no fixture
        private readonly int[] _indexInFixture;
        private readonly int[] _fixtureLength;
        private readonly float[] _diagonal;            // x - y of the pixel's image position: constant along a 45° line
        private readonly bool[] _soloed;               // the pixel is in the soloed fixture (all true with no solo)
        private readonly bool[] _blanked;              // the fixture blanks the pixel: black in every pattern

        private PixelOutputOverride(PixelOutputOverrideRequest request, int[] ordinal, int[] index, int[] length, float[] diagonal, bool[] soloed, bool[] blanked)
        {
            Request = request;
            _fixtureOrdinal = ordinal;
            _indexInFixture = index;
            _fixtureLength = length;
            _diagonal = diagonal;
            _soloed = soloed;
            _blanked = blanked;
        }

        public PixelOutputOverrideRequest Request { get; }

        public int PixelCount => _fixtureOrdinal.Length;

        /// <summary>True when any pixel's colour is replaced; false means the animation passes through untouched.</summary>
        public bool IsActive => Request.IsActive;

        public static PixelOutputOverride Compile(PixelMapLayout layout, PixelOutputOverrideRequest request)
        {
            if (layout == null) throw new ArgumentNullException(nameof(layout));

            int count = layout.TotalPixelCount;
            var ordinal = new int[count];
            var index = new int[count];
            var length = new int[count];
            var diagonal = new float[count];
            var soloed = new bool[count];
            var blanked = new bool[count];
            Array.Fill(ordinal, -1);

            int fixtureOrdinal = 0;
            foreach (var slot in layout.Slots)
            {
                if (slot.Kind != LayoutSlotKind.Fixture || slot.Fixture == null)
                {
                    continue;
                }
                bool solo = !request.SoloFixtureId.HasValue || slot.Fixture.Id == request.SoloFixtureId.Value;
                for (int i = 0; i < slot.PixelCount; i++)
                {
                    int p = slot.PixelStart + i;
                    ordinal[p] = fixtureOrdinal;
                    index[p] = i;
                    length[p] = slot.PixelCount;
                    diagonal[p] = DiagonalOf(slot.Fixture, i);
                    soloed[p] = solo;
                    blanked[p] = slot.Fixture.IsBlanked(i);
                }
                fixtureOrdinal++;
            }

            return new PixelOutputOverride(request, ordinal, index, length, diagonal, soloed, blanked);
        }

        /// <summary>
        /// Whether <paramref name="pixelIndex"/>'s colour comes from here rather than the animation:
        /// every pixel while a pattern is on, and every pixel outside the soloed fixture otherwise.
        /// </summary>
        public bool Replaces(int pixelIndex)
        {
            if (Request.Pattern != PixelTestPattern.None)
            {
                return true;
            }
            return Request.SoloFixtureId.HasValue && !_soloed[pixelIndex];
        }

        /// <summary>The colour for <paramref name="pixelIndex"/> at <paramref name="seconds"/>, for a pixel
        /// <see cref="Replaces"/>. A blanked pixel is black in every pattern, as it is in the animation —
        /// the pattern is for checking the wiring the map describes, dead tail included.</summary>
        public RgbSample ColorOf(int pixelIndex, double seconds)
        {
            if (!_soloed[pixelIndex] || _fixtureOrdinal[pixelIndex] < 0 || _blanked[pixelIndex])
            {
                return Black;
            }
            return PatternColor(Request.Pattern, _fixtureOrdinal[pixelIndex], _indexInFixture[pixelIndex], _fixtureLength[pixelIndex], _diagonal[pixelIndex], seconds);
        }

        /// <summary>
        /// The same answer from a fixture rather than a frame index, for a preview that walks the
        /// map instead of the frame: null when the animation shows through, otherwise the colour
        /// the rig shows for that pixel of <paramref name="fixture"/>, which is the
        /// <paramref name="fixtureOrdinal"/>th enabled fixture of the map.
        /// </summary>
        public static RgbSample? ColorFor(PixelOutputOverrideRequest request, FixtureDefinition fixture, int fixtureOrdinal, int indexInFixture, double seconds)
        {
            if (fixture == null) throw new ArgumentNullException(nameof(fixture));
            bool soloed = !request.SoloFixtureId.HasValue || request.SoloFixtureId.Value == fixture.Id;
            if (request.Pattern == PixelTestPattern.None)
            {
                return soloed ? null : Black;
            }
            return soloed && !fixture.IsBlanked(indexInFixture)
                ? PatternColor(request.Pattern, fixtureOrdinal, indexInFixture, fixture.PixelCount, DiagonalOf(fixture, indexInFixture), seconds)
                : Black;
        }

        /// <summary>The coordinate the diagonals are drawn in: x − y of the pixel's image
        /// position, which every point on one 45° line (down and to the right) shares.</summary>
        private static float DiagonalOf(FixtureDefinition fixture, int indexInFixture)
        {
            var at = PixelMapGeometry.Resolve(fixture, indexInFixture);
            return at.X - at.Y;
        }

        private static RgbSample PatternColor(PixelTestPattern pattern, int fixtureOrdinal, int i, int n, float diagonal, double seconds)
        {
            switch (pattern)
            {
                case PixelTestPattern.Chase:
                {
                    int cycle = n + ChaseTrail + ChaseRest;
                    int head = (int)(seconds * ChasePixelsPerSecond % cycle);
                    int behind = head - i;                    // 0 at the head, growing down the trail
                    if (behind < 0 || behind > ChaseTrail)
                    {
                        return Background;
                    }
                    byte level = (byte)(255 * (ChaseTrail + 1 - behind) / (ChaseTrail + 1));
                    return new RgbSample(level, level, level);
                }
                case PixelTestPattern.Rainbow:
                    return Hue(n > 1 ? (double)i / n : 0);
                case PixelTestPattern.FixtureColors:
                    return Hue(fixtureOrdinal * 0.381966 % 1.0);
                case PixelTestPattern.White:
                    return new RgbSample(255, 255, 255);
                case PixelTestPattern.Diagonals:
                {
                    // Static on purpose: a line that moved could not be lined up against.
                    // Each line takes the next colour of the cycle, so the line a strip
                    // shows can be named and found on the strip beside it.
                    int line = (int)Math.Floor(diagonal / DiagonalPeriod);
                    float across = diagonal - line * DiagonalPeriod;          // 0..period, from the line's leading edge
                    if (across >= DiagonalWidth)
                    {
                        return Background;
                    }
                    int cycle = DiagonalColors.Length;
                    return DiagonalColors[(line % cycle + cycle) % cycle];
                }
                default:
                    return Black;
            }
        }

        /// <summary>Full-saturation, full-value colour at <paramref name="hue"/> turns.</summary>
        private static RgbSample Hue(double hue)
        {
            double h = (hue % 1.0 + 1.0) % 1.0 * 6.0;
            int sector = (int)h;
            double f = h - sector;
            byte up = (byte)Math.Round(255 * f);
            byte down = (byte)Math.Round(255 * (1 - f));
            return sector switch
            {
                0 => new RgbSample(255, up, 0),
                1 => new RgbSample(down, 255, 0),
                2 => new RgbSample(0, 255, up),
                3 => new RgbSample(0, down, 255),
                4 => new RgbSample(up, 0, 255),
                _ => new RgbSample(255, 0, down),
            };
        }
    }
}
