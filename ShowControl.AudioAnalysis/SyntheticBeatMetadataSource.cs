namespace ShowControl.AudioAnalysis;

/// <summary>
/// A hand-driven <see cref="IBeatMetadataSource"/> for tests and the debug view: set a grid
/// and sections, clear them, emit beat packets — the same surface the native Pro DJ Link
/// port will fill from the wire. Publishes immutable state by reference swap, so it is safe
/// to poke from a UI thread while the analysis thread reads.
/// </summary>
public sealed class SyntheticBeatMetadataSource : IBeatMetadataSource
{
    private volatile BeatGrid? _grid;
    private volatile IReadOnlyList<SectionMarker> _sections = Array.Empty<SectionMarker>();
    private volatile TrackAnalysis? _trackAnalysis;
    private BeatMetadataPresence _presence = BeatMetadataPresence.Absent;

    public BeatMetadataPresence Presence => _presence;
    public BeatGrid? CurrentGrid => _grid;
    public IReadOnlyList<SectionMarker> Sections => _sections;
    public TrackAnalysis? TrackAnalysis => _trackAnalysis;
    public event Action<BeatObservation>? BeatObserved;

    /// <summary>Publish a full-track grid (with optional sections), as a track load would.</summary>
    public void SetGrid(BeatGrid grid, IReadOnlyList<SectionMarker>? sections = null, TrackAnalysis? trackAnalysis = null)
    {
        _grid = grid;
        _sections = sections ?? Array.Empty<SectionMarker>();
        _trackAnalysis = trackAnalysis;
        _presence = BeatMetadataPresence.FullGrid;
    }

    /// <summary>Drop to beat-packets-only, as losing track data but keeping the wire would.</summary>
    public void SetBeatsOnly()
    {
        _grid = null;
        _sections = Array.Empty<SectionMarker>();
        _trackAnalysis = null;
        _presence = BeatMetadataPresence.BeatsOnly;
    }

    /// <summary>Go dark, as unplugging the gear would.</summary>
    public void Clear()
    {
        _grid = null;
        _sections = Array.Empty<SectionMarker>();
        _trackAnalysis = null;
        _presence = BeatMetadataPresence.Absent;
    }

    /// <summary>Emit one beat packet.</summary>
    public void EmitBeat(BeatObservation beat) => BeatObserved?.Invoke(beat);
}
