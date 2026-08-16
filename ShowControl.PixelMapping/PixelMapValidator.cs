using System;
using System.Collections.Generic;

namespace ShowControl.PixelMapping
{
    /// <summary>
    /// Rejects maps that could not be rendered. This is the first of the three guards that keep a
    /// bad edit from reaching the render thread, where a size mismatch would throw out of the
    /// render loop and freeze the lights mid-show.
    /// </summary>
    public static class PixelMapValidator
    {
        public static IReadOnlyList<string> Validate(PixelMap map, IPixelTypeResolver pixelTypes)
        {
            if (map == null) throw new ArgumentNullException(nameof(map));
            if (pixelTypes == null) throw new ArgumentNullException(nameof(pixelTypes));

            var problems = new List<string>();

            if (map.Version > PixelMap.CurrentVersion)
            {
                problems.Add($"Map version {map.Version} is newer than this build understands ({PixelMap.CurrentVersion}).");
            }

            if (map.ImageWidth != PixelMap.DefaultImageWidth || map.ImageHeight != PixelMap.DefaultImageHeight)
            {
                problems.Add(
                    $"Map is authored for a {map.ImageWidth}x{map.ImageHeight} shader image but the render target is " +
                    $"{PixelMap.DefaultImageWidth}x{PixelMap.DefaultImageHeight}.");
            }

            var seenIds = new HashSet<Guid>();
            // Enabled fixtures grouped by output, so channel overlap is checked per endpoint.
            var occupied = new Dictionary<OutputTargetKey, List<(int Start, int End, string Name)>>();

            foreach (var fixture in map.Fixtures)
            {
                string label = string.IsNullOrWhiteSpace(fixture.Name) ? fixture.Id.ToString() : fixture.Name;

                if (!seenIds.Add(fixture.Id))
                {
                    problems.Add($"Fixture '{label}' repeats id {fixture.Id}.");
                }

                if (fixture.PixelOffsets.Length == 0)
                {
                    problems.Add($"Fixture '{label}' has no pixels.");
                }
                else if (fixture.PixelOffsets.Length % 2 != 0)
                {
                    problems.Add($"Fixture '{label}' has {fixture.PixelOffsets.Length} offset values; an x and a y are needed per pixel.");
                }

                foreach (var blank in fixture.BlankedPixels)
                {
                    if (blank.Count <= 0 || blank.Start < 0 || blank.EndExclusive > fixture.PixelCount)
                    {
                        problems.Add(
                            $"Fixture '{label}' blanks pixels {blank.Start}..{blank.EndExclusive - 1}, outside its {fixture.PixelCount} pixels.");
                    }
                }

                if (!pixelTypes.TryGetChannelCount(fixture.PixelType, out int channelsPerPixel))
                {
                    problems.Add(
                        $"Fixture '{label}' uses unknown pixel type '{fixture.PixelType}'. Known types: {string.Join(", ", pixelTypes.KnownPixelTypes)}.");
                    continue;
                }

                if (string.IsNullOrWhiteSpace(fixture.Output.IpAddress))
                {
                    problems.Add($"Fixture '{label}' has no output IP address.");
                }

                if (fixture.Output.StartChannel < 0)
                {
                    problems.Add($"Fixture '{label}' has a negative start channel ({fixture.Output.StartChannel}).");
                }

                if (!fixture.Enabled || fixture.PixelCount == 0)
                {
                    continue;
                }

                var key = fixture.Output.Key;
                if (!occupied.TryGetValue(key, out var ranges))
                {
                    ranges = new List<(int, int, string)>();
                    occupied.Add(key, ranges);
                }

                int start = fixture.Output.StartChannel;
                int end = start + (fixture.PixelCount * channelsPerPixel);
                foreach (var (otherStart, otherEnd, otherName) in ranges)
                {
                    if (start < otherEnd && otherStart < end)
                    {
                        problems.Add(
                            $"Fixture '{label}' occupies channels {start}..{end - 1} on {key}, overlapping '{otherName}' at {otherStart}..{otherEnd - 1}.");
                    }
                }
                ranges.Add((start, end, label));
            }

            return problems;
        }

        public static bool IsValid(PixelMap map, IPixelTypeResolver pixelTypes, out IReadOnlyList<string> problems)
        {
            problems = Validate(map, pixelTypes);
            return problems.Count == 0;
        }
    }
}
