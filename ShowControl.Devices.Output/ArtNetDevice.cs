using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ArtNetSharp;
using ArtNetSharp.Messages;
using ArtNetSharp.Misc;
using ArtNetSharp.Communication;
using System.Net;
using org.dmxc.wkdt.Network;
using System.Threading;

namespace ShowControl.Devices.Output
{
    public class ArtNetDevice : IDmxOutputDevice
    {
        static ControllerInstance controllerInstance = new ControllerInstance(ArtNet.Instance);
        private byte _universe;
        private byte _sequence = 1;
        private byte[] _ipAddress;
        public int MaxChannels { get; private set; }
        static ArtNetDevice()
        {
            controllerInstance = new ControllerInstance(ArtNet.Instance);
            controllerInstance.Name = controllerInstance.ShortName = "ShowControl";
            controllerInstance.AddPortConfig(
                new PortConfig(1, new org.dmxc.wkdt.Light.ArtNet.PortAddress(0), false, true) { PortNumber = (byte)0, Type = EPortType.InputToArtNet | EPortType.ArtNet });
            controllerInstance.AddPortConfig(
                new PortConfig(2, new org.dmxc.wkdt.Light.ArtNet.PortAddress(1), false, true) { PortNumber = (byte)1, Type = EPortType.InputToArtNet | EPortType.ArtNet });
            controllerInstance.AddPortConfig(
                new PortConfig(3, new org.dmxc.wkdt.Light.ArtNet.PortAddress(0), false, true) { PortNumber = (byte)0, Type = EPortType.InputToArtNet | EPortType.ArtNet, ForceBroadcast = false });

            //controllerInstance.RemoteClientDiscovered += ControllerInstance_RemoteClientDiscovered;
            ArtNet.Instance.AddInstance(controllerInstance);
            var broadcastIp = new IPAddress(new byte[] { 192, 168, 3, 255 });
            ArtNet.Instance.NetworkClients.ToList().ForEach((ncb) =>
            {
                ncb.Enabled = IPAddress.Equals(broadcastIp, ncb.BroadcastIpAddress);
            });
        }

        public static void SendSync()
        {
            controllerInstance.SendArtSync().Wait();
        }
        public ArtNetDevice(byte universe, byte[] ipAddress, int maxChannels)
        {
            MaxChannels = maxChannels;
            _universe = universe;
            _ipAddress = ipAddress;
        }

        public void Write(byte[] dmxData)
        {
            // if (dmxData.Length < 512)
            // {
            //     Array.Resize(ref dmxData, 512);
            //     // for (int i = 511; i >= 2; i--)
            //     // {
            //     //     dmxData[i] = dmxData[i - 2];
            //     // }
            //     // dmxData[1] = 0;
            //     // dmxData[0] = 0;
            // }
            controllerInstance.sendArtDMX(new IPv4Address(_ipAddress), new org.dmxc.wkdt.Light.ArtNet.PortAddress(_universe), 0, dmxData, (byte)(_sequence + 1)).Wait();
            _sequence = (byte)((_sequence + 1) % 254);
        }
    }
}
