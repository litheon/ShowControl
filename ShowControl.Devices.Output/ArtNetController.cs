using System;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using ArtNetSharp;
using ArtNetSharp.Communication;
using ArtNetSharp.Messages;
using ArtNetSharp.Misc;
using Microsoft.Extensions.Logging;
using org.dmxc.wkdt.Network;

namespace ShowControl.Devices.Output
{
    public class ArtNetController : IArtNetController
    {
        private readonly ControllerInstance _controllerInstance;
        private readonly ILogger<ArtNetController> _logger;

        // ArtNet.Instance discovers network interfaces via `async void updateNetworkClients()`,
        // which returns at its first await and populates NetworkClients on a threadpool thread.
        // Sending before any client exists silently drops packets — TrySendPacket iterates zero
        // entries and returns. This task completes once NetworkClients is populated, and is
        // awaited at the top of every send path. After the first completion, awaiting an already-
        // completed Task is free.
        private readonly Task _readyTask;

        public ArtNetController(ILoggerFactory loggerFactory)
        {
            _logger = loggerFactory.CreateLogger<ArtNetController>();

            // Wire ArtNetSharp's internal logger to our ILoggerFactory so errors from
            // NetworkClientBag.GetLocalIP, openClient, TrySendPacket etc. surface in our output.
            // Must happen before the first ArtNet.Instance access to ensure the factory is used
            // from the start. The setter throws if called more than once.
            try { ArtNetSharp.Logging.LoggerFactory = loggerFactory; }
            catch (InvalidOperationException) { _logger.LogWarning("ArtNetSharp.Logging.LoggerFactory was already set — internal ArtNet logs may not be visible."); }

            _controllerInstance = new ControllerInstance(ArtNet.Instance);
            _controllerInstance.Name = _controllerInstance.ShortName = "ShowControl";
            _controllerInstance.AddPortConfig(
                new PortConfig(1, new org.dmxc.wkdt.Light.ArtNet.PortAddress(0), false, true) { PortNumber = (byte)0, Type = EPortType.InputToArtNet | EPortType.ArtNet });
            _controllerInstance.AddPortConfig(
                new PortConfig(2, new org.dmxc.wkdt.Light.ArtNet.PortAddress(1), false, true) { PortNumber = (byte)1, Type = EPortType.InputToArtNet | EPortType.ArtNet });
            _controllerInstance.AddPortConfig(
                new PortConfig(3, new org.dmxc.wkdt.Light.ArtNet.PortAddress(0), false, true) { PortNumber = (byte)0, Type = EPortType.InputToArtNet | EPortType.ArtNet, ForceBroadcast = false });

            ArtNet.Instance.AddInstance(_controllerInstance);
            _readyTask = WaitForNetworkClientsAsync();
        }

        private async Task WaitForNetworkClientsAsync()
        {
            var initWatch = Stopwatch.StartNew();
            while (!ArtNet.Instance.NetworkClients.Any())
                await Task.Delay(50);

            var clients = ArtNet.Instance.NetworkClients.ToList();
            _logger.LogInformation("ArtNet: {Count} NetworkClient(s) ready after {Ms}ms", clients.Count, initWatch.ElapsedMilliseconds);
            foreach (var ncb in clients)
                _logger.LogInformation("  NetworkClient LocalIP={LocalIP} BroadcastIP={BroadcastIP} Enabled={Enabled} ClientAlive={Alive}",
                    ncb.LocalIpAddress, ncb.BroadcastIpAddress, ncb.Enabled, ncb.ClientAlive);

            // Restrict sending to the 192.168.3.x subnet interface only.
            var broadcastIp = new IPAddress(new byte[] { 192, 168, 3, 255 });
            foreach (var ncb in clients)
            {
                ncb.Enabled = IPAddress.Equals(broadcastIp, ncb.BroadcastIpAddress);
                _logger.LogInformation("  Set NetworkClient LocalIP={LocalIP} Enabled={Enabled}", ncb.LocalIpAddress, ncb.Enabled);
            }
        }

        public async Task SendArtDMX(byte[] ipAddress, byte universe, byte sourcePort, byte[] data, byte sequence)
        {
            await _readyTask;
            await _controllerInstance.sendArtDMX(
                new IPv4Address(ipAddress),
                new org.dmxc.wkdt.Light.ArtNet.PortAddress(universe),
                sourcePort,
                data,
                sequence);
        }

        public async Task SendSync()
        {
            await _readyTask;
            await _controllerInstance.SendArtSync();
        }
    }
}
