using System;
using BeatLink;
using BeatLink.Data;
using Microsoft.Extensions.Logging.Abstractions;
using ShowControl.AudioAnalysis;
using ShowControl.Devices.ProDjLink;
using Xunit;
using LinkBeatGrid = BeatLink.Data.BeatGrid;

namespace ShowControl.Tests;

/// <summary>
/// Drives the real BeatLinkNet singletons (through their internal injection seams — no network)
/// and the real adapter, reproducing the live scenario: a playing deck whose tempo slider moves.
/// The adapter is constructed BEFORE the TimeFinder starts, matching the app's DI ordering, so
/// these tests also cover the event-subscription-order behavior of the production wiring.
/// </summary>
[Collection("Pro DJ Link singletons")]
public sealed class ProDjLinkTempoResponseTests : IDisposable
{
    private const int Player = 2;

    public ProDjLinkTempoResponseTests()
    {
        ResetState();
    }

    public void Dispose()
    {
        ResetState();
    }

    private static void ResetState()
    {
        TimeFinder.Instance.Stop();
        BeatGridFinder.Instance.Stop();
        MetadataFinder.Instance.Stop();
        BeatLink.DbServer.ConnectionManager.Instance.Stop();
        BeatFinder.Instance.Stop();
        BeatFinder.Instance.PortOverride = null;
        DeviceFinder.Instance.Stop();
        DeviceFinder.Instance.PortOverride = null;
        VirtualCdj.Instance.ResetForTests();
        LifecycleParticipant.WaitForPendingAnnouncements(TimeSpan.FromSeconds(5));
    }

    private static LinkBeatGrid EightBeatGrid()
    {
        var reference = new DataReference(Player, CdjStatus.TrackSourceSlot.UsbSlot, 42,
            CdjStatus.TrackType.Rekordbox);
        return new LinkBeatGrid(reference,
            beatWithinBarValues: [1, 2, 3, 4, 1, 2, 3, 4],
            bpmValues: [12000, 12000, 12000, 12000, 12000, 12000, 12000, 12000],
            timeWithinTrackValues: [0, 500, 1000, 1500, 2000, 2500, 3000, 3500]);
    }

    /// <summary>A playing nexus CDJ status for the fixture track at the given pitch.</summary>
    private static CdjStatus PlayingStatus(double pitchMultiplier, int beatNumber)
    {
        var bytes = new byte[0xd4];
        Util.MagicHeader.CopyTo(bytes);
        bytes[Util.PacketTypeOffset] = 0x0a;
        "CDJ-2000nexus"u8.CopyTo(bytes.AsSpan(0x0b));
        bytes[0x21] = Player;
        Util.NumberToBytes(0xd4 - 0x24, bytes, 0x22, 2);   // payload length
        bytes[0x27] = 1;                                    // busy
        bytes[0x28] = Player;                               // track source player
        bytes[0x29] = 3;                                    // slot: USB
        bytes[0x2a] = 1;                                    // track type: rekordbox
        Util.NumberToBytes(42, bytes, 0x2c, 4);             // rekordbox id (matches the grid)
        bytes[0x7b] = 3;                                    // P1: playing
        bytes[0x89] = 0x40;                                 // F: playing
        bytes[0x8b] = 0x7a;                                 // P2: moving
        Util.NumberToBytes((long)Math.Round(pitchMultiplier * Util.NeutralPitch), bytes, 0x8d, 3);
        Util.NumberToBytes(12000, bytes, 0x92, 2);          // BPM * 100
        bytes[0x9d] = 9;                                    // P3: forward
        bytes[0x9f] = 0xff;                                 // no master handoff
        Util.NumberToBytes(beatNumber, bytes, 0xa0, 4);
        bytes[0xa6] = (byte)(((beatNumber - 1) % 4) + 1);
        return new CdjStatus(bytes, System.Net.IPAddress.Parse("192.168.86.42"));
    }

    [Fact]
    public void TempoSliderChangeRepublishesGridAtNewRate()
    {
        // Construct the adapter first — exactly as DI does in the app — so its status handler
        // is subscribed ahead of the TimeFinder's.
        var clock = 1000.0;
        var adapter = new ProDjLinkBeatMetadataSource(() => clock,
            NullLogger<ProDjLinkBeatMetadataSource>.Instance);

        DeviceFinder.Instance.PortOverride = 0;
        BeatFinder.Instance.PortOverride = 0;
        VirtualCdj.Instance.SetRunningStateForTests(true);
        TimeFinder.Instance.Start();
        BeatGridFinder.Instance.InjectBeatGridForTests(Player, EightBeatGrid());

        // Deck starts playing at neutral pitch. Two status packets, as on the wire, so both the
        // TimeFinder and the adapter (whichever is notified first) have consistent data.
        VirtualCdj.Instance.ProcessUpdate(PlayingStatus(1.0, beatNumber: 2));
        clock += 0.2;
        VirtualCdj.Instance.ProcessUpdate(PlayingStatus(1.0, beatNumber: 2));

        Assert.Equal(BeatMetadataPresence.FullGrid, adapter.Presence);
        var initialGrid = adapter.CurrentGrid;
        Assert.NotNull(initialGrid);
        Assert.Equal(120.0, initialGrid.BpmAt(clock), 1);

        // The DJ moves the tempo slider to +6%. Allow the two status packets the wire would
        // deliver (the first teaches the TimeFinder the new pitch, the second lets the adapter
        // see it, given the app's subscription ordering).
        clock += 0.2;
        VirtualCdj.Instance.ProcessUpdate(PlayingStatus(1.06, beatNumber: 3));
        clock += 0.2;
        VirtualCdj.Instance.ProcessUpdate(PlayingStatus(1.06, beatNumber: 3));

        var retunedGrid = adapter.CurrentGrid;
        Assert.NotNull(retunedGrid);
        Assert.NotSame(initialGrid, retunedGrid);
        Assert.Equal(120.0 * 1.06, retunedGrid.BpmAt(clock), 1);

        // And the PLL — the consumer that turns the grid into the published Bpm — follows.
        var pll = new TempoPll();
        pll.AttachMetadata(adapter);
        pll.Advance(clock);
        Assert.Equal(120.0 * 1.06, pll.Bpm, 1);
    }

    [Fact]
    public void BeatPacketsCarryEffectiveTempoWithoutVirtualCdjStatus()
    {
        // Regression guard for the OnBeat fragility: beat observations must flow (with the
        // pitched tempo) even when the VirtualCdj has no status for the player, because
        // Beat.IsBeatWithinBarMeaningful would throw in that situation and the BeatFinder's
        // handler isolation would swallow it silently.
        var clock = 500.0;
        var adapter = new ProDjLinkBeatMetadataSource(() => clock,
            NullLogger<ProDjLinkBeatMetadataSource>.Instance);
        BeatObservation? observed = null;
        adapter.BeatObserved += beat => observed = beat;

        var bytes = new byte[0x60];
        Util.MagicHeader.CopyTo(bytes);
        bytes[Util.PacketTypeOffset] = 0x28;
        "CDJ-2000nexus"u8.CopyTo(bytes.AsSpan(0x0b));
        bytes[0x21] = Player;
        Util.NumberToBytes((long)Math.Round(1.06 * Util.NeutralPitch), bytes, 0x55, 3);
        Util.NumberToBytes(12000, bytes, 0x5a, 2);          // BPM * 100 = 120.00
        bytes[0x5c] = 3;                                    // beat within bar
        var beat = new Beat(bytes, System.Net.IPAddress.Parse("192.168.86.42"));

        BeatFinder.Instance.DeliverBeatForTests(beat);

        Assert.NotNull(observed);
        Assert.Equal(120.0 * 1.06, observed.Value.Bpm, 1);
        Assert.Equal(3, observed.Value.BeatWithinBar);
    }
}
