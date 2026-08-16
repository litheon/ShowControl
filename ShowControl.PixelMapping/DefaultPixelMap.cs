using System;
using System.IO;
using System.Reflection;

namespace ShowControl.PixelMapping
{
    /// <summary>
    /// The map shipped with the build, reproducing the venue layout that used to be hardcoded.
    /// <para>
    /// Embedded rather than a MAUI asset: <c>Resources/Raw</c> only exists in the MAUI app, and the
    /// console shell and the WPF panel start the same host.
    /// </para>
    /// </summary>
    public static class DefaultPixelMap
    {
        private const string ResourceName = "ShowControl.PixelMapping.DefaultPixelMap.json";

        public static string ReadJson()
        {
            var assembly = typeof(DefaultPixelMap).GetTypeInfo().Assembly;
            using var stream = assembly.GetManifestResourceStream(ResourceName)
                ?? throw new InvalidOperationException(
                    $"Embedded resource '{ResourceName}' is missing. Available: {string.Join(", ", assembly.GetManifestResourceNames())}");
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }

        public static PixelMap Load() => PixelMapSerializer.Deserialize(ReadJson());
    }
}
