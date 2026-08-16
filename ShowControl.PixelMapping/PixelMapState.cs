using System;

namespace ShowControl.PixelMapping
{
    /// <summary>
    /// Sidecar recorded next to the working copy, describing what the user has and has not chosen.
    /// <para>
    /// Content hashes rather than a bare dirty flag, so a process killed part-way through a write
    /// is detectable on the next launch instead of being silently trusted.
    /// </para>
    /// </summary>
    public sealed class PixelMapState
    {
        /// <summary>Last file the user explicitly saved to, if any.</summary>
        public string? SavedPath { get; set; }

        public string? SavedContentHash { get; set; }

        public string? WorkingContentHash { get; set; }

        /// <summary>True when the working copy holds edits the user has not saved anywhere.</summary>
        public bool IsDirty { get; set; }

        public DateTimeOffset? LastAutosaveUtc { get; set; }
    }
}
