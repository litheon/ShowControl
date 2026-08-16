using System;
using System.Collections.Generic;
using ShowControl.Devices.DMX;
using ShowControl.PixelMapping;

namespace ShowControl.Rendering
{
    /// <summary>
    /// Bridges pixel type names in a saved map to the DMX fixture catalogue.
    /// <para>
    /// This lives here rather than in <c>ShowControl.PixelMapping</c> because that project stays
    /// dependency-free so <c>ShowControl.Animation</c> and the plain-net9.0 test project can both
    /// consume the map.
    /// </para>
    /// </summary>
    public sealed class PixelTypeRegistry : IPixelTypeResolver
    {
        public static readonly PixelTypeRegistry Default = new PixelTypeRegistry();

        private readonly Dictionary<string, Func<IDmxDevice>> _factories =
            new Dictionary<string, Func<IDmxDevice>>(StringComparer.OrdinalIgnoreCase)
            {
                ["WS2812B"] = () => new WS2812B(),
                ["AsteraAX3"] = () => new AsteraAX3(),
                ["AsteraTitan"] = () => new AsteraTitan(),
                ["BetopperDJ"] = () => new BetopperDJ(),
                ["BetopperDJNew"] = () => new BetopperDJNew(),
                ["ChauvetFreedom"] = () => new ChauvetFreedom(),
                ["JLPow"] = () => new JLPow(),
                ["Missyee"] = () => new Missyee(),
                ["Sheds"] = () => new Sheds()
            };

        private readonly Dictionary<string, int> _channelCounts;

        public PixelTypeRegistry()
        {
            _channelCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var pair in _factories)
            {
                _channelCounts[pair.Key] = pair.Value().ChannelCount;
            }
        }

        public bool TryGetChannelCount(string pixelType, out int channelCount) =>
            _channelCounts.TryGetValue(pixelType ?? string.Empty, out channelCount);

        public IReadOnlyList<string> KnownPixelTypes
        {
            get
            {
                var names = new List<string>(_factories.Keys);
                names.Sort(StringComparer.OrdinalIgnoreCase);
                return names;
            }
        }

        /// <summary>
        /// Builds a fresh device instance. Deliberately not the <c>Instance</c> singletons: those are
        /// shared mutable state, and a run of pixels needs its own so per-fixture settings cannot
        /// leak between fixtures.
        /// </summary>
        public IDmxDevice Create(string pixelType)
        {
            if (!_factories.TryGetValue(pixelType ?? string.Empty, out var factory))
            {
                throw new ArgumentException(
                    $"Unknown pixel type '{pixelType}'. Known types: {string.Join(", ", KnownPixelTypes)}.",
                    nameof(pixelType));
            }
            return factory();
        }
    }
}
