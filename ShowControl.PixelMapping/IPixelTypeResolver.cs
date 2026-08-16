using System.Collections.Generic;

namespace ShowControl.PixelMapping
{
    /// <summary>
    /// Maps a pixel type name to its channel count.
    /// <para>
    /// This project stays dependency-free on purpose — <c>ShowControl.Animation</c> cannot see
    /// <c>ShowControl.Devices.DMX</c>, and the test project is plain net9.0 — so the DMX fixture
    /// catalogue is supplied from outside rather than referenced. <c>ShowControl.Rendering</c>
    /// provides the real implementation; tests provide a small fake.
    /// </para>
    /// </summary>
    public interface IPixelTypeResolver
    {
        bool TryGetChannelCount(string pixelType, out int channelCount);

        IReadOnlyList<string> KnownPixelTypes { get; }
    }
}
