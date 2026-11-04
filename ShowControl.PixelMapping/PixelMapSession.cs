using System;
using System.Collections.Generic;
using System.Threading;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace ShowControl.PixelMapping
{
    /// <summary>
    /// Owns the live pixel map: what is loaded, whether it has unsaved edits, and publishing edits
    /// to whoever is rendering.
    /// </summary>
    public sealed class PixelMapSession : IPixelMapEditor
    {
        private sealed class Snapshot
        {
            public Snapshot(PixelMap map, PixelSampleMap sampleMap, PixelMapLayout layout, PixelMapSummary summary)
            {
                Map = map;
                SampleMap = sampleMap;
                Layout = layout;
                Summary = summary;
            }

            public PixelMap Map { get; }
            public PixelSampleMap SampleMap { get; }
            public PixelMapLayout Layout { get; }
            public PixelMapSummary Summary { get; }
        }

        private readonly IPixelTypeResolver _pixelTypes;
        private readonly ILogger _logger;
        private readonly object _saveLock = new object();

        private Snapshot _snapshot;
        private PixelMapState _state;

        private PixelMapSession(IPixelTypeResolver pixelTypes, ILogger logger, PixelMap map, PixelMapState state, bool recoveredUnsavedWork)
        {
            _pixelTypes = pixelTypes;
            _logger = logger;
            _state = state;
            RecoveredUnsavedWork = recoveredUnsavedWork;
            _snapshot = Compile(map);
        }

        public PixelMap Current => Volatile.Read(ref _snapshot).Map;

        public PixelSampleMap CurrentSampleMap => Volatile.Read(ref _snapshot).SampleMap;

        public PixelMapLayout CurrentLayout => Volatile.Read(ref _snapshot).Layout;

        public PixelMapSummary CurrentSummary => Volatile.Read(ref _snapshot).Summary;

        public bool IsDirty => _state.IsDirty;

        public string? SavedPath => _state.SavedPath;

        public bool RecoveredUnsavedWork { get; private set; }

        public event EventHandler<PixelMapChangedEventArgs>? Changed;

        // Boxed: the request is wider than a word, and the render thread reads it while the
        // editor writes it, so the publish has to be one reference write.
        private object _outputOverride = PixelOutputOverrideRequest.Nothing;

        public PixelOutputOverrideRequest CurrentOutputOverride => (PixelOutputOverrideRequest)Volatile.Read(ref _outputOverride);

        public event EventHandler? OutputOverrideChanged;

        public void SetOutputOverride(PixelOutputOverrideRequest request)
        {
            if (CurrentOutputOverride == request)
            {
                return;
            }
            Volatile.Write(ref _outputOverride, request);
            OutputOverrideChanged?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// Resolves which map to run with, preferring unsaved work the user was never given a
        /// chance to keep. Each fallback is logged, and a corrupt, missing or unreadable file at
        /// any tier falls through to the next so the app still starts at a venue. Unreadable
        /// matters as much as corrupt on Mac Catalyst: the sandbox grant for a user-picked save
        /// location dies with the session that picked it, so the saved file is routinely
        /// inaccessible on the next launch — the working copy inside the container, which every
        /// save also writes, is where the same content is still readable.
        /// </summary>
        public static PixelMapSession LoadAtStartup(IPixelTypeResolver pixelTypes, ILogger? logger = null)
        {
            if (pixelTypes == null) throw new ArgumentNullException(nameof(pixelTypes));
            logger ??= NullLogger.Instance;

            PixelMapState state = ReadState(logger);

            if (state.IsDirty &&
                TryReadTier(PixelMapPaths.WorkingCopy, logger, out string workingJson) &&
                WorkingCopyHashMatches(workingJson, state, logger) &&
                TryDeserialize(workingJson, PixelMapPaths.WorkingCopy, pixelTypes, logger, out var recovered))
            {
                logger.LogInformation(
                    "[PixelMap] Recovered unsaved edits from {Path}. The user was never asked whether to keep them.",
                    PixelMapPaths.WorkingCopy);
                return new PixelMapSession(pixelTypes, logger, recovered!, state, recoveredUnsavedWork: true);
            }

            if (!string.IsNullOrWhiteSpace(state.SavedPath))
            {
                if (TryReadTier(state.SavedPath, logger, out string savedJson) &&
                    TryDeserialize(savedJson, state.SavedPath!, pixelTypes, logger, out var saved))
                {
                    logger.LogInformation("[PixelMap] Loaded {Path}.", state.SavedPath);
                    state.IsDirty = false;
                    return new PixelMapSession(pixelTypes, logger, saved!, state, recoveredUnsavedWork: false);
                }

                // The saved file is gone or, on Catalyst, simply not readable this session. Every
                // save also wrote the working copy, so the same content is still in the container;
                // running with it keeps the rig on the venue's map instead of the built-in default.
                // SavedPath is kept so the status line stays honest — a plain Save back to it may
                // still be denied, and Save As is the recovery.
                if (TryReadTier(PixelMapPaths.WorkingCopy, logger, out string fallbackJson) &&
                    WorkingCopyHashMatches(fallbackJson, state, logger) &&
                    TryDeserialize(fallbackJson, PixelMapPaths.WorkingCopy, pixelTypes, logger, out var fallback))
                {
                    logger.LogWarning(
                        "[PixelMap] {Path} could not be loaded; running with the working copy, which holds the same content.",
                        state.SavedPath);
                    return new PixelMapSession(pixelTypes, logger, fallback!, state, recoveredUnsavedWork: false);
                }
            }

            logger.LogInformation("[PixelMap] Using the built-in default map.");
            return new PixelMapSession(pixelTypes, logger, DefaultPixelMap.Load(), new PixelMapState(), recoveredUnsavedWork: false);
        }

        /// <summary>
        /// A tier's read: false rather than a throw for a file that is missing or unreadable, so
        /// startup falls through to the next tier. A read denied by the sandbox lands here — an
        /// exception escaping this method would take down not just the map but the whole host,
        /// because it is thrown inside a DI factory.
        /// </summary>
        private static bool TryReadTier(string? path, ILogger logger, out string contents)
        {
            try
            {
                return PixelMapFile.TryRead(path, out contents);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "[PixelMap] Could not read {Path}; trying the next fallback.", path);
                contents = string.Empty;
                return false;
            }
        }

        private static bool WorkingCopyHashMatches(string workingJson, PixelMapState state, ILogger logger)
        {
            string hash = PixelMapSerializer.ComputeHash(workingJson);
            if (string.Equals(hash, state.WorkingContentHash, StringComparison.Ordinal))
            {
                return true;
            }

            logger.LogWarning(
                "[PixelMap] Working copy at {Path} does not match the hash recorded when it was written; ignoring it.",
                PixelMapPaths.WorkingCopy);
            return false;
        }

        /// <summary>Builds a session around a map already in hand. For tests and for the console shell.</summary>
        public static PixelMapSession FromMap(PixelMap map, IPixelTypeResolver pixelTypes, ILogger? logger = null) =>
            new PixelMapSession(pixelTypes, logger ?? NullLogger.Instance, map, new PixelMapState(), recoveredUnsavedWork: false);

        public IReadOnlyList<string> Apply(PixelMap updated)
        {
            if (updated == null) throw new ArgumentNullException(nameof(updated));

            var problems = PixelMapValidator.Validate(updated, _pixelTypes);
            if (problems.Count > 0)
            {
                _logger.LogWarning("[PixelMap] Rejected an edit with {Count} problem(s): {Problems}", problems.Count, string.Join("; ", problems));
                return problems;
            }

            // Clone defensively so the editor can keep mutating its own working instance between
            // applies without touching what is live.
            var published = updated.Clone();
            var previous = Volatile.Read(ref _snapshot);
            var next = Compile(published);
            var kind = Classify(previous.Map, published);

            Volatile.Write(ref _snapshot, next);
            _state.IsDirty = true;
            RecoveredUnsavedWork = false;

            Changed?.Invoke(this, new PixelMapChangedEventArgs(kind, next.Map, next.SampleMap, next.Layout));
            return Array.Empty<string>();
        }

        public void SaveWorkingCopy()
        {
            lock (_saveLock)
            {
                try
                {
                    string json = PixelMapSerializer.Serialize(Current);
                    PixelMapFile.WriteAtomic(PixelMapPaths.WorkingCopy, json);
                    _state.WorkingContentHash = PixelMapSerializer.ComputeHash(json);
                    _state.LastAutosaveUtc = DateTimeOffset.UtcNow;
                    WriteState();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[PixelMap] Could not autosave the working copy to {Path}.", PixelMapPaths.WorkingCopy);
                }
            }
        }

        public void Save(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("Path is required.", nameof(path));

            lock (_saveLock)
            {
                string json = PixelMapSerializer.Serialize(Current);
                // In place, not atomic: the destination is wherever the user chose, and under the
                // Mac Catalyst sandbox the grant covers that file only — creating a sibling .tmp
                // next to it is denied. The working copy below keeps the atomic write.
                PixelMapFile.WriteInPlace(path, json);

                string hash = PixelMapSerializer.ComputeHash(json);
                _state.SavedPath = path;
                _state.SavedContentHash = hash;
                _state.WorkingContentHash = hash;
                _state.IsDirty = false;
                RecoveredUnsavedWork = false;

                // Keep the working copy in step so a later crash recovers the saved content.
                PixelMapFile.WriteAtomic(PixelMapPaths.WorkingCopy, json);
                WriteState();
            }

            _logger.LogInformation("[PixelMap] Saved to {Path}.", path);
        }

        public PixelMap Open(string path)
        {
            if (!PixelMapFile.TryRead(path, out string json))
            {
                throw new PixelMapFormatException($"Pixel map file '{path}' does not exist.");
            }

            var map = PixelMapSerializer.Deserialize(json);
            var problems = PixelMapValidator.Validate(map, _pixelTypes);
            if (problems.Count > 0)
            {
                throw new PixelMapFormatException($"'{path}' is not usable: {string.Join("; ", problems)}");
            }

            var previous = Volatile.Read(ref _snapshot);
            var next = Compile(map);
            var kind = Classify(previous.Map, map);
            Volatile.Write(ref _snapshot, next);

            lock (_saveLock)
            {
                _state.SavedPath = path;
                _state.SavedContentHash = PixelMapSerializer.ComputeHash(json);
                _state.WorkingContentHash = _state.SavedContentHash;
                _state.IsDirty = false;
                RecoveredUnsavedWork = false;
                PixelMapFile.WriteAtomic(PixelMapPaths.WorkingCopy, json);
                WriteState();
            }

            Changed?.Invoke(this, new PixelMapChangedEventArgs(kind, next.Map, next.SampleMap, next.Layout));
            _logger.LogInformation("[PixelMap] Opened {Path}.", path);
            return next.Map;
        }

        public void KeepRecoveredWork()
        {
            RecoveredUnsavedWork = false;
        }

        public void DiscardRecoveredWork()
        {
            RecoveredUnsavedWork = false;

            PixelMap replacement;
            if (!string.IsNullOrWhiteSpace(_state.SavedPath) &&
                PixelMapFile.TryRead(_state.SavedPath, out string savedJson) &&
                TryDeserialize(savedJson, _state.SavedPath!, _pixelTypes, _logger, out var saved))
            {
                replacement = saved!;
            }
            else
            {
                replacement = DefaultPixelMap.Load();
                _state.SavedPath = null;
                _state.SavedContentHash = null;
            }

            var next = Compile(replacement);
            Volatile.Write(ref _snapshot, next);

            lock (_saveLock)
            {
                _state.IsDirty = false;
                PixelMapFile.WriteAtomic(PixelMapPaths.WorkingCopy, PixelMapSerializer.Serialize(replacement));
                _state.WorkingContentHash = PixelMapSerializer.ComputeHash(PixelMapSerializer.Serialize(replacement));
                WriteState();
            }

            Changed?.Invoke(this, new PixelMapChangedEventArgs(PixelMapChangeKind.Structural, next.Map, next.SampleMap, next.Layout));
            _logger.LogInformation("[PixelMap] Discarded recovered edits.");
        }

        /// <summary>
        /// Geometry when the rig's shape is identical and only positions moved. Everything else is
        /// structural, because buffer sizes, the surface tree or the device list have to change.
        /// </summary>
        internal static PixelMapChangeKind Classify(PixelMap previous, PixelMap updated)
        {
            if (previous.Fixtures.Count != updated.Fixtures.Count ||
                previous.ImageWidth != updated.ImageWidth ||
                previous.ImageHeight != updated.ImageHeight)
            {
                return PixelMapChangeKind.Structural;
            }

            for (int i = 0; i < previous.Fixtures.Count; i++)
            {
                var a = previous.Fixtures[i];
                var b = updated.Fixtures[i];

                if (a.Id != b.Id ||
                    a.Enabled != b.Enabled ||
                    a.PixelCount != b.PixelCount ||
                    a.ColorOrder != b.ColorOrder ||
                    !string.Equals(a.PixelType, b.PixelType, StringComparison.Ordinal) ||
                    a.Output.StartChannel != b.Output.StartChannel ||
                    !a.Output.Key.Equals(b.Output.Key))
                {
                    return PixelMapChangeKind.Structural;
                }
            }

            return PixelMapChangeKind.Geometry;
        }

        private Snapshot Compile(PixelMap map)
        {
            var layout = PixelMapLayoutBuilder.Build(map, _pixelTypes);
            var sampleMap = PixelSampleMapBuilder.Build(map, layout);
            var summary = PixelMapSummary.FromLayout(map, layout, _pixelTypes);
            return new Snapshot(map, sampleMap, layout, summary);
        }

        private void WriteState()
        {
            try
            {
                PixelMapFile.WriteAtomic(PixelMapPaths.State, PixelMapSerializer.SerializeState(_state));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[PixelMap] Could not write {Path}.", PixelMapPaths.State);
            }
        }

        private static PixelMapState ReadState(ILogger logger)
        {
            try
            {
                if (PixelMapFile.TryRead(PixelMapPaths.State, out string json))
                {
                    return PixelMapSerializer.DeserializeState(json);
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "[PixelMap] Could not read {Path}; starting from a clean state.", PixelMapPaths.State);
            }
            return new PixelMapState();
        }

        private static bool TryDeserialize(
            string json,
            string path,
            IPixelTypeResolver pixelTypes,
            ILogger logger,
            out PixelMap? map)
        {
            map = null;
            try
            {
                var candidate = PixelMapSerializer.Deserialize(json);
                var problems = PixelMapValidator.Validate(candidate, pixelTypes);
                if (problems.Count > 0)
                {
                    logger.LogWarning("[PixelMap] {Path} is not usable: {Problems}", path, string.Join("; ", problems));
                    return false;
                }
                map = candidate;
                return true;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "[PixelMap] Could not read {Path}.", path);
                return false;
            }
        }
    }
}
