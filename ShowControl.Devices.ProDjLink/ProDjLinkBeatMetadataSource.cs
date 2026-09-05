using BeatLink;
using BeatLink.CrateDigger.Pdb;
using Microsoft.Extensions.Logging;
using ShowControl.AudioAnalysis;
using LinkBeatGrid = BeatLink.Data.BeatGrid;

namespace ShowControl.Devices.ProDjLink;

/// <summary>
/// The native Pro DJ Link implementation of <see cref="IBeatMetadataSource"/>, filling the
/// surface <see cref="SyntheticBeatMetadataSource"/> stood in for: beat packets, the loaded
/// track's rekordbox beat grid, and phrase (song-structure) sections, all translated from the
/// players' media timeline onto the local capture clock as the contract requires.
///
/// Follows one deck — the tempo master when there is one, otherwise the lowest-numbered playing
/// player — and re-anchors the published grid as that deck's interpolated playback position
/// drifts (position updates arrive ~every 200 ms, or every 30 ms from a CDJ-3000). Per the
/// design rule the grid is a prior for the PLL, so an anchor error within
/// <c>TempoPll.MaxGridTrimSeconds</c> is absorbed by local onsets.
/// </summary>
public sealed class ProDjLinkBeatMetadataSource : IBeatMetadataSource
{
    /// <summary>Republish the grid when the anchor prediction drifts by more than this.</summary>
    internal const double ReanchorThresholdSeconds = 0.010;

    /// <summary>Below this playback rate the deck is effectively stopped and its grid cannot be
    /// projected onto advancing local time.</summary>
    internal const double MinimumPlaybackRate = 0.05;

    private readonly Func<double> _localClock;
    private readonly ILogger _logger;

    private sealed record PublishedState(
        int Player,
        BeatGrid Grid,
        IReadOnlyList<SectionMarker> Sections,
        LinkBeatGrid LinkGrid,
        double AnchorLocalSeconds,
        double TrackSecondsAtAnchor,
        double PlaybackRate);

    private volatile PublishedState? _published;
    private volatile int _deviceCount;
    private int _followedPlayer;

    /// <summary>Latest song-structure entries per player, so sections republish with the grid.</summary>
    private readonly Dictionary<int, RekordboxAnlz.TaggedSection> _songStructures = new();
    private readonly object _rebuildLock = new();

    /// <param name="localClock">Returns the current local capture-clock time in seconds (the
    /// analysis engine's <c>Latest.Time</c>). Must be callable from network threads.</param>
    /// <param name="logger">Where integration events are reported.</param>
    public ProDjLinkBeatMetadataSource(Func<double> localClock, ILogger<ProDjLinkBeatMetadataSource> logger)
    {
        _localClock = localClock;
        _logger = logger;

        // The BeatLinkNet services are process singletons; subscribing before they start is
        // fine (handlers only fire while they run), and they outlive this singleton.
        DeviceFinder.Instance.DeviceFound += (_, _) => OnDevicesChanged();
        DeviceFinder.Instance.DeviceLost += (_, _) => OnDevicesChanged();
        BeatFinder.Instance.BeatReceived += OnBeat;
        BeatFinder.Instance.PrecisePositionReceived += (_, _) => MaybeReanchor();
        VirtualCdj.Instance.DeviceUpdateReceived += OnDeviceUpdate;
        BeatLink.Data.BeatGridFinder.Instance.BeatGridChanged += (_, update) => OnTrackDataChanged(update.Player);
        BeatLink.Data.MetadataFinder.Instance.TrackMetadataChanged += (_, update) => OnTrackDataChanged(update.Player);
        BeatLink.Data.AnalysisTagFinder.Instance.AddAnalysisTagListener(OnSongStructure, ".EXT", "PSSI");
    }

    public BeatMetadataPresence Presence
    {
        get
        {
            if (_published != null)
            {
                return BeatMetadataPresence.FullGrid;
            }
            return _deviceCount > 0 ? BeatMetadataPresence.BeatsOnly : BeatMetadataPresence.Absent;
        }
    }

    public BeatGrid? CurrentGrid => _published?.Grid;

    public IReadOnlyList<SectionMarker> Sections => _published?.Sections ?? Array.Empty<SectionMarker>();

    /// <summary>Shape-only in the contract and consumed by nothing yet, so not populated;
    /// rekordbox waveform data is available from the port when something wants it.</summary>
    public TrackAnalysis? TrackAnalysis => null;

    public event Action<BeatObservation>? BeatObserved;

    private void OnDevicesChanged()
    {
        _deviceCount = DeviceFinder.Instance.IsRunning ? DeviceFinder.Instance.CurrentDevices.Count : 0;
        if (_deviceCount == 0 && _published != null)
        {
            _published = null;
            _logger.LogInformation("All DJ Link devices gone; beat metadata now absent.");
        }
    }

    /// <summary>Translates a BeatLinkNet monotonic timestamp (nanoseconds) to the local capture
    /// clock by subtracting its age from the capture clock's present.</summary>
    private double LocalTimeOf(long monotonicNanos) =>
        _localClock() - (Util.CurrentTimestampNanos() - monotonicNanos) / 1_000_000_000.0;

    private void OnBeat(object? sender, Beat beat)
    {
        // Runs on the BeatFinder receive thread — keep it lean.
        var followed = Volatile.Read(ref _followedPlayer);
        if (followed != 0 && beat.DeviceNumber != followed)
        {
            return;
        }
        var beatWithinBar = beat.IsBeatWithinBarMeaningful ? beat.BeatWithinBar : 0;
        BeatObserved?.Invoke(new BeatObservation(
            LocalTimeOf(beat.Timestamp), (float)beat.EffectiveTempo, beatWithinBar, 0.9f));
        MaybeReanchor();
    }

    private void OnDeviceUpdate(object? sender, DeviceUpdate update)
    {
        // Runs on the VirtualCdj status receive thread — cheap checks only, with the grid
        // rebuild gated behind the drift threshold.
        if (update is CdjStatus)
        {
            UpdateFollowedPlayer();
            MaybeReanchor();
        }
    }

    private void OnTrackDataChanged(int player)
    {
        if (player == Volatile.Read(ref _followedPlayer))
        {
            Rebuild(force: true);
        }
    }

    private void OnSongStructure(object? sender, BeatLink.Data.AnalysisTagUpdate update)
    {
        lock (_rebuildLock)
        {
            if (update.TaggedSection == null)
            {
                _songStructures.Remove(update.Player);
            }
            else
            {
                _songStructures[update.Player] = update.TaggedSection;
            }
        }
        OnTrackDataChanged(update.Player);
    }

    /// <summary>Picks the deck the published grid follows: the tempo master when known,
    /// otherwise the lowest-numbered playing player.</summary>
    private void UpdateFollowedPlayer()
    {
        var chosen = 0;
        if (VirtualCdj.Instance.IsRunning)
        {
            try
            {
                chosen = VirtualCdj.Instance.TempoMaster?.DeviceNumber ?? 0;
            }
            catch (InvalidOperationException)
            {
                // Stopped between the check and the read; treated as no master.
            }
            if (chosen == 0)
            {
                foreach (var candidate in VirtualCdj.Instance.LatestStatus)
                {
                    if (candidate is CdjStatus { IsPlaying: true } status &&
                        (chosen == 0 || status.DeviceNumber < chosen))
                    {
                        chosen = status.DeviceNumber;
                    }
                }
            }
        }
        var previous = Interlocked.Exchange(ref _followedPlayer, chosen);
        if (previous != chosen)
        {
            _logger.LogInformation("Following deck {Player} for beat metadata.", chosen);
            Rebuild(force: true);
        }
    }

    /// <summary>Republishes the grid if the followed deck's interpolated position has drifted
    /// from what the current anchor predicts (or nothing is published yet).</summary>
    private void MaybeReanchor()
    {
        var published = _published;
        if (published == null)
        {
            Rebuild(force: false);
            return;
        }
        var position = BeatLink.Data.TimeFinder.Instance.IsRunning
            ? BeatLink.Data.TimeFinder.Instance.GetLatestPositionFor(published.Player)
            : null;
        if (position == null)
        {
            return;
        }
        var rate = PlaybackRateOf(position);
        if (rate < MinimumPlaybackRate)
        {
            _published = null;  // Deck stopped; grid time can no longer be projected.
            return;
        }
        var actualLocal = LocalTimeOf(position.Timestamp);
        var predictedLocal = published.AnchorLocalSeconds +
            (position.Milliseconds / 1000.0 - published.TrackSecondsAtAnchor) / published.PlaybackRate;
        if (Math.Abs(actualLocal - predictedLocal) > ReanchorThresholdSeconds ||
            Math.Abs(rate - published.PlaybackRate) > 0.001)
        {
            Rebuild(force: true);
        }
    }

    private static double PlaybackRateOf(BeatLink.Data.TrackPositionUpdate position) =>
        position.Playing && !position.Reverse ? position.Pitch : 0.0;

    /// <summary>Builds and publishes a fresh grid + sections for the followed deck, or clears
    /// the published state when the deck has no usable grid or is not playing forward.</summary>
    private void Rebuild(bool force)
    {
        lock (_rebuildLock)
        {
            var player = Volatile.Read(ref _followedPlayer);
            if (!force && _published != null)
            {
                return;
            }
            if (player == 0 || !BeatLink.Data.TimeFinder.Instance.IsRunning)
            {
                _published = null;
                return;
            }
            var position = BeatLink.Data.TimeFinder.Instance.GetLatestPositionFor(player);
            var linkGrid = position?.BeatGrid ??
                (BeatLink.Data.BeatGridFinder.Instance.IsRunning
                    ? BeatLink.Data.BeatGridFinder.Instance.GetLatestBeatGridFor(player)
                    : null);
            if (position == null || linkGrid == null || linkGrid.BeatCount < 2)
            {
                _published = null;
                return;
            }
            var rate = PlaybackRateOf(position);
            if (rate < MinimumPlaybackRate)
            {
                _published = null;
                return;
            }

            var anchorLocal = LocalTimeOf(position.Timestamp);
            var trackSecondsAtAnchor = position.Milliseconds / 1000.0;
            var grid = ProjectGrid(linkGrid, anchorLocal, trackSecondsAtAnchor, rate);

            IReadOnlyList<SectionMarker> sections = Array.Empty<SectionMarker>();
            if (_songStructures.TryGetValue(player, out var structure))
            {
                sections = MapSections(structure, linkGrid, anchorLocal, trackSecondsAtAnchor, rate);
            }

            _published = new PublishedState(player, grid, sections, linkGrid,
                anchorLocal, trackSecondsAtAnchor, rate);
        }
    }

    /// <summary>
    /// Projects a rekordbox beat grid (times on the track's own timeline, in milliseconds) onto
    /// the local capture clock: a beat at track time <c>t</c> lands at
    /// <c>anchor + (t - trackAtAnchor) / rate</c> local seconds.
    /// </summary>
    internal static BeatGrid ProjectGrid(
        LinkBeatGrid linkGrid, double anchorLocalSeconds, double trackSecondsAtAnchor, double playbackRate)
    {
        var times = new double[linkGrid.BeatCount];
        var bars = new int[linkGrid.BeatCount];
        for (var beat = 1; beat <= linkGrid.BeatCount; beat++)
        {
            times[beat - 1] = anchorLocalSeconds +
                (linkGrid.GetTimeWithinTrack(beat) / 1000.0 - trackSecondsAtAnchor) / playbackRate;
            bars[beat - 1] = Math.Clamp(linkGrid.GetBeatWithinBar(beat), 1, 4);
        }
        return new BeatGrid(times, bars, confidence: 1f);
    }

    /// <summary>
    /// Maps rekordbox phrase analysis onto <see cref="SectionMarker"/>s on the local clock.
    /// High-mood tracks read as EDM phrasing, so their choruses are reported as drops.
    /// </summary>
    internal static IReadOnlyList<SectionMarker> MapSections(
        RekordboxAnlz.TaggedSection songStructure, LinkBeatGrid linkGrid,
        double anchorLocalSeconds, double trackSecondsAtAnchor, double playbackRate)
    {
        if (songStructure.Body is not RekordboxAnlz.SongStructureTag tag)
        {
            return Array.Empty<SectionMarker>();
        }
        var body = tag.Body;
        var markers = new List<SectionMarker>(body.Entries.Count);
        foreach (var entry in body.Entries)
        {
            if (entry.Beat < 1 || entry.Beat > linkGrid.BeatCount)
            {
                continue;
            }
            var localTime = anchorLocalSeconds +
                (linkGrid.GetTimeWithinTrack(entry.Beat) / 1000.0 - trackSecondsAtAnchor) / playbackRate;
            markers.Add(new SectionMarker(localTime, MapKind(body.Mood, entry), 0.9f));
        }
        return markers;
    }

    internal static SectionKind MapKind(RekordboxAnlz.TrackMood mood, RekordboxAnlz.SongStructureEntry entry) =>
        entry.Kind switch
        {
            RekordboxAnlz.PhraseHigh high when mood == RekordboxAnlz.TrackMood.High => MapKindCore(mood, (int)high.Id),
            RekordboxAnlz.PhraseMid mid when mood == RekordboxAnlz.TrackMood.Mid => MapKindCore(mood, (int)mid.Id),
            RekordboxAnlz.PhraseLow low when mood == RekordboxAnlz.TrackMood.Low => MapKindCore(mood, (int)low.Id),
            _ => SectionKind.Unknown,
        };

    /// <summary>The mood/phrase-id mapping itself, on plain values so it can be tested without
    /// constructing Kaitai parse trees.</summary>
    internal static SectionKind MapKindCore(RekordboxAnlz.TrackMood mood, int phraseId)
    {
        switch (mood)
        {
            case RekordboxAnlz.TrackMood.High:
                return (RekordboxAnlz.MoodHighPhrase)phraseId switch
                {
                    RekordboxAnlz.MoodHighPhrase.Intro => SectionKind.Intro,
                    RekordboxAnlz.MoodHighPhrase.Up => SectionKind.BuildUp,
                    RekordboxAnlz.MoodHighPhrase.Down => SectionKind.Breakdown,
                    // High-mood tracks read as EDM phrasing, where the chorus is the drop.
                    RekordboxAnlz.MoodHighPhrase.Chorus => SectionKind.Drop,
                    RekordboxAnlz.MoodHighPhrase.Outro => SectionKind.Outro,
                    _ => SectionKind.Unknown,
                };
            case RekordboxAnlz.TrackMood.Mid:
                return (RekordboxAnlz.MoodMidPhrase)phraseId switch
                {
                    RekordboxAnlz.MoodMidPhrase.Intro => SectionKind.Intro,
                    RekordboxAnlz.MoodMidPhrase.Bridge => SectionKind.Breakdown,
                    RekordboxAnlz.MoodMidPhrase.Chorus => SectionKind.Chorus,
                    RekordboxAnlz.MoodMidPhrase.Outro => SectionKind.Outro,
                    _ => SectionKind.Verse,
                };
            case RekordboxAnlz.TrackMood.Low:
                return (RekordboxAnlz.MoodLowPhrase)phraseId switch
                {
                    RekordboxAnlz.MoodLowPhrase.Intro => SectionKind.Intro,
                    RekordboxAnlz.MoodLowPhrase.Bridge => SectionKind.Breakdown,
                    RekordboxAnlz.MoodLowPhrase.Chorus => SectionKind.Chorus,
                    RekordboxAnlz.MoodLowPhrase.Outro => SectionKind.Outro,
                    _ => SectionKind.Verse,
                };
            default:
                return SectionKind.Unknown;
        }
    }
}
