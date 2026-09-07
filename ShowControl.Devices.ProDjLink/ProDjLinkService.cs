using System.Net.Sockets;
using BeatLink;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ShowControl.Devices.ProDjLink;

/// <summary>
/// Keeps the Pro DJ Link stack running for the life of the app: the discovery listeners start
/// immediately, and the full metadata chain (VirtualCdj, metadata/beat-grid/analysis finders,
/// TimeFinder, CrateDigger NFS downloads) is brought up in the background once DJ Link devices
/// actually appear, then re-attempted whenever the network changes. Startup problems — most
/// commonly the DJ Link ports being held by rekordbox running on this machine — degrade to
/// "no beat metadata" rather than failing the host.
/// </summary>
public sealed class ProDjLinkService : IHostedService
{
    private static readonly TimeSpan RetryInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan PortConflictRetryInterval = TimeSpan.FromSeconds(60);

    private readonly ILogger<ProDjLinkService> _logger;
    private readonly CancellationTokenSource _stopping = new();
    private Task? _runner;
    private bool _portConflictReported;

    public ProDjLinkService(ILogger<ProDjLinkService> logger, ILoggerFactory loggerFactory,
        ProDjLinkBeatMetadataSource beatMetadataSource)
    {
        _logger = logger;
        BeatLinkLogging.LoggerFactory = loggerFactory;
        // The source subscribes to the wire in its constructor; requiring it here guarantees it
        // exists before any packets can arrive.
        _ = beatMetadataSource;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _runner = Task.Run(() => RunAsync(_stopping.Token), CancellationToken.None);
        return Task.CompletedTask;
    }

    private async Task RunAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            var delay = RetryInterval;
            try
            {
                if (!DeviceFinder.Instance.IsRunning)
                {
                    DeviceFinder.Instance.Start();
                    BeatFinder.Instance.Start();
                    _portConflictReported = false;
                    _logger.LogInformation("Pro DJ Link discovery listening.");
                }

                if (DeviceFinder.Instance.CurrentDevices.Count > 0 && !VirtualCdj.Instance.IsRunning)
                {
                    if (VirtualCdj.Instance.Start())
                    {
                        _logger.LogInformation("Joined the DJ Link network as device {Number}.",
                            VirtualCdj.Instance.DeviceNumber);
                    }
                }

                if (VirtualCdj.Instance.IsRunning)
                {
                    BeatLink.Data.MetadataFinder.Instance.Start();
                    BeatLink.Data.BeatGridFinder.Instance.Start();
                    BeatLink.Data.AnalysisTagFinder.Instance.Start();
                    BeatLink.Data.WaveformFinder.Instance.Start();
                    BeatLink.Data.TimeFinder.Instance.Start();
                    BeatLink.Data.CrateDigger.Instance.Start();
                }
            }
            catch (SocketException e)
            {
                // Almost always: rekordbox (or another Pro DJ Link app) holds the ports.
                if (!_portConflictReported)
                {
                    _logger.LogWarning(
                        "Cannot open the Pro DJ Link ports ({Message}) — is rekordbox running on this machine? " +
                        "Beat metadata stays offline; retrying every {Interval}.",
                        e.Message, PortConflictRetryInterval);
                    _portConflictReported = true;
                }
                delay = PortConflictRetryInterval;
            }
            catch (Exception e)
            {
                _logger.LogWarning(e, "Problem bringing up the Pro DJ Link stack; will retry.");
            }

            try
            {
                await Task.Delay(delay, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _stopping.Cancel();
        if (_runner != null)
        {
            try
            {
                await _runner.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Shutdown timed out waiting for the loop; keep going and stop the services.
            }
        }
        BeatLink.Data.CrateDigger.Instance.Stop();
        BeatLink.Data.TimeFinder.Instance.Stop();
        BeatLink.Data.WaveformFinder.Instance.Stop();
        BeatLink.Data.AnalysisTagFinder.Instance.Stop();
        BeatLink.Data.BeatGridFinder.Instance.Stop();
        BeatLink.Data.MetadataFinder.Instance.Stop();
        VirtualCdj.Instance.Stop();
        BeatFinder.Instance.Stop();
        DeviceFinder.Instance.Stop();
    }
}
