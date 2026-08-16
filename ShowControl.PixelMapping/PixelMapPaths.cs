using System;
using System.IO;

namespace ShowControl.PixelMapping
{
    /// <summary>
    /// Where the working copy lives. Mirrors <c>LoggingBootstrap.LogDirectory</c>, so it resolves
    /// inside the sandbox container on Mac Catalyst and under %AppData% on Windows.
    /// </summary>
    public static class PixelMapPaths
    {
        public static string Directory =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ShowControl");

        /// <summary>Continuously autosaved copy of whatever the user is editing.</summary>
        public static string WorkingCopy => Path.Combine(Directory, "pixelmap.working.json");

        /// <summary>Records what the user has and has not chosen to save.</summary>
        public static string State => Path.Combine(Directory, "pixelmap.state.json");
    }
}
