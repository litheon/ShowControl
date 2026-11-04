using System;
using System.Collections.Generic;

namespace ShowControl.PixelMapping
{
    public enum PixelMapChangeKind
    {
        /// <summary>
        /// Positions moved but the pixel count, fixture set and output targets are unchanged. Can be
        /// applied to a running render loop with a single volatile write.
        /// </summary>
        Geometry = 0,

        /// <summary>
        /// Fixtures, pixel counts, pixel types or output targets changed, so buffers, surfaces and
        /// devices all have to be rebuilt.
        /// </summary>
        Structural
    }

    public sealed class PixelMapChangedEventArgs : EventArgs
    {
        public PixelMapChangedEventArgs(PixelMapChangeKind kind, PixelMap map, PixelSampleMap sampleMap, PixelMapLayout layout)
        {
            Kind = kind;
            Map = map;
            SampleMap = sampleMap;
            Layout = layout;
        }

        public PixelMapChangeKind Kind { get; }

        public PixelMap Map { get; }

        public PixelSampleMap SampleMap { get; }

        public PixelMapLayout Layout { get; }
    }

    /// <summary>Read side of the live pixel map, consumed by the render path.</summary>
    public interface IPixelMapSource
    {
        PixelMap Current { get; }

        PixelSampleMap CurrentSampleMap { get; }

        PixelMapLayout CurrentLayout { get; }

        PixelMapSummary CurrentSummary { get; }

        /// <summary>
        /// What the output shows instead of the animation — a test pattern, a soloed fixture —
        /// for checking the wiring. Session state, never saved with the map. Read once per
        /// frame by the render path and compiled against the current layout there.
        /// </summary>
        PixelOutputOverrideRequest CurrentOutputOverride { get; }

        /// <summary>Raised on whatever thread set the override; the render path only reads the property.</summary>
        event EventHandler? OutputOverrideChanged;
    }

    /// <summary>Write side, consumed by the editor UI.</summary>
    public interface IPixelMapEditor : IPixelMapSource
    {
        /// <summary>True when the working copy holds edits the user has not saved to a named file.</summary>
        bool IsDirty { get; }

        /// <summary>Last file the user explicitly saved to, if any.</summary>
        string? SavedPath { get; }

        /// <summary>
        /// True when startup restored a working copy the user never chose to keep or discard —
        /// the prompt they did not get on the way out.
        /// </summary>
        bool RecoveredUnsavedWork { get; }

        event EventHandler<PixelMapChangedEventArgs> Changed;

        /// <summary>
        /// Validates and publishes an edited map. Returns the validation problems; an empty list
        /// means it was applied. Nothing invalid is ever published, which is the first of the
        /// guards keeping a bad edit away from the render thread.
        /// </summary>
        IReadOnlyList<string> Apply(PixelMap updated);

        /// <summary>Flushes the current map to the working copy. Safe to call often.</summary>
        void SaveWorkingCopy();

        void Save(string path);

        PixelMap Open(string path);

        /// <summary>Accepts the recovered working copy without saving it anywhere named.</summary>
        void KeepRecoveredWork();

        /// <summary>Throws away recovered edits and reloads the last saved file, or the default map.</summary>
        void DiscardRecoveredWork();

        /// <summary>Sets what the output shows instead of the animation. Not validated against the
        /// map: a solo of a fixture that no longer exists simply blanks everything, which is the
        /// honest result, and the editor clears it on leaving edit mode.</summary>
        void SetOutputOverride(PixelOutputOverrideRequest request);
    }
}
