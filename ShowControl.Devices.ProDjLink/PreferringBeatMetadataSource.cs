using ShowControl.AudioAnalysis;

namespace ShowControl.Devices.ProDjLink;

/// <summary>
/// Presents the Pro DJ Link source when it has anything to say, and falls back to another
/// source (the debug panel's synthetic one) when the wire is dark — so the debug Grid button
/// keeps working with no CDJs on the network, and real gear takes precedence the moment it
/// appears. Beat packets are forwarded only from whichever source is currently active, so the
/// PLL never fuses two disagreeing timelines.
/// </summary>
public sealed class PreferringBeatMetadataSource : IBeatMetadataSource
{
    private readonly IBeatMetadataSource _preferred;
    private readonly IBeatMetadataSource _fallback;

    public PreferringBeatMetadataSource(IBeatMetadataSource preferred, IBeatMetadataSource fallback)
    {
        _preferred = preferred;
        _fallback = fallback;
        _preferred.BeatObserved += beat =>
        {
            if (Active == _preferred)
            {
                BeatObserved?.Invoke(beat);
            }
        };
        _fallback.BeatObserved += beat =>
        {
            if (Active == _fallback)
            {
                BeatObserved?.Invoke(beat);
            }
        };
    }

    private IBeatMetadataSource Active =>
        _preferred.Presence != BeatMetadataPresence.Absent ? _preferred : _fallback;

    public BeatMetadataPresence Presence => Active.Presence;

    public BeatGrid? CurrentGrid => Active.CurrentGrid;

    public IReadOnlyList<SectionMarker> Sections => Active.Sections;

    public IReadOnlyList<SectionMarker> DerivedMarkers => Active.DerivedMarkers;

    public TrackAnalysis? TrackAnalysis => Active.TrackAnalysis;

    public event Action<BeatObservation>? BeatObserved;
}
