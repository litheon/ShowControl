using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace ShowControl.PixelMapping
{
    /// <summary>
    /// A run of pixel indices within one fixture. Used for dead tails, which occur as runs
    /// rather than as scattered individuals.
    /// </summary>
    public readonly record struct PixelRange(int Start, int Count)
    {
        public int EndExclusive => Start + Count;

        public bool Contains(int pixelIndex) => pixelIndex >= Start && pixelIndex < Start + Count;

        /// <summary>
        /// The editor's notation for a list of ranges: zero-based, inclusive, <c>12-15, 40, 100-103</c>.
        /// A single index is a range of one. An en dash is accepted as well as a hyphen, since
        /// that is what the readout prints. Empty text is no ranges.
        /// </summary>
        public static string Format(IEnumerable<PixelRange> ranges)
        {
            if (ranges == null) throw new ArgumentNullException(nameof(ranges));
            var sb = new StringBuilder();
            foreach (var range in ranges)
            {
                if (sb.Length > 0)
                {
                    sb.Append(", ");
                }
                sb.Append(range.Start.ToString(CultureInfo.InvariantCulture));
                if (range.Count > 1)
                {
                    sb.Append('-').Append((range.EndExclusive - 1).ToString(CultureInfo.InvariantCulture));
                }
            }
            return sb.ToString();
        }

        /// <summary>
        /// Parses the notation <see cref="Format"/> writes, in any order, and hands back the ranges
        /// sorted and merged — two that touch or overlap become one — so the list is canonical
        /// whatever was typed. Every index must lie within <paramref name="pixelCount"/>.
        /// </summary>
        /// <returns>False with <paramref name="error"/> set, naming the offending piece, if the text
        /// is not a list of ranges or one reaches outside the fixture.</returns>
        public static bool TryParseList(string? text, int pixelCount, out List<PixelRange> ranges, out string? error)
        {
            ranges = new List<PixelRange>();
            error = null;
            if (string.IsNullOrWhiteSpace(text))
            {
                return true;
            }

            var pieces = text.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (string piece in pieces)
            {
                string[] ends = piece.Split(new[] { '-', '–', '—' }, StringSplitOptions.None);
                if (ends.Length > 2
                    || !int.TryParse(ends[0].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int first)
                    || (ends.Length == 2 && !int.TryParse(ends[1].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out _)))
                {
                    error = $"'{piece}' is not a pixel index or a range like 12-15.";
                    return false;
                }
                int last = ends.Length == 2 ? int.Parse(ends[1].Trim(), CultureInfo.InvariantCulture) : first;
                if (last < first)
                {
                    (first, last) = (last, first);
                }
                if (first < 0 || last >= pixelCount)
                {
                    error = $"'{piece}' is outside the fixture's {pixelCount} pixels (0-{pixelCount - 1}).";
                    return false;
                }
                ranges.Add(new PixelRange(first, last - first + 1));
            }

            ranges = Merge(ranges);
            return true;
        }

        /// <summary>Sorted, with touching and overlapping ranges joined.</summary>
        public static List<PixelRange> Merge(IEnumerable<PixelRange> ranges)
        {
            var merged = new List<PixelRange>();
            foreach (var range in ranges.Where(r => r.Count > 0).OrderBy(r => r.Start))
            {
                if (merged.Count > 0 && range.Start <= merged[^1].EndExclusive)
                {
                    var last = merged[^1];
                    int end = Math.Max(last.EndExclusive, range.EndExclusive);
                    merged[^1] = new PixelRange(last.Start, end - last.Start);
                }
                else
                {
                    merged.Add(range);
                }
            }
            return merged;
        }

        /// <summary>
        /// The ranges cut down to a fixture of <paramref name="pixelCount"/> pixels: what a shrink
        /// leaves of them. Those entirely beyond the end are dropped, one straddling it is
        /// shortened.
        /// </summary>
        public static List<PixelRange> ClipTo(IEnumerable<PixelRange> ranges, int pixelCount)
        {
            var clipped = new List<PixelRange>();
            foreach (var range in ranges)
            {
                int end = Math.Min(range.EndExclusive, pixelCount);
                if (end > range.Start && range.Start >= 0)
                {
                    clipped.Add(new PixelRange(range.Start, end - range.Start));
                }
            }
            return clipped;
        }
    }
}
