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
    internal class ArtNetDevice : IDmxOutputDevice
    {
        ControllerInstance controllerInstance = new ControllerInstance(ArtNet.Instance);
        private byte _universe;
        public ArtNetDevice(byte universe)
        {
            _universe = universe;
            var broadcastIp = new IPAddress(new byte[] { 192, 168, 3, 255 });
            ArtNet.Instance.NetworkClients.ToList().ForEach((ncb) => {
                ncb.Enabled = IPAddress.Equals(broadcastIp, ncb.BroadcastIpAddress);
            });
            controllerInstance.Name = controllerInstance.ShortName = "ShowControl";
            controllerInstance.AddPortConfig(
                new PortConfig(1, new org.dmxc.wkdt.Light.ArtNet.PortAddress(0), false, true) { PortNumber = (byte)0, Type = EPortType.InputToArtNet | EPortType.ArtNet });
            controllerInstance.AddPortConfig(
                new PortConfig(2, new org.dmxc.wkdt.Light.ArtNet.PortAddress(1), false, true) { PortNumber = (byte)1, Type = EPortType.InputToArtNet | EPortType.ArtNet });
            controllerInstance.AddPortConfig(
                new PortConfig(3, new org.dmxc.wkdt.Light.ArtNet.PortAddress(0),  false, true) { PortNumber = (byte)0, Type = EPortType.InputToArtNet | EPortType.ArtNet, ForceBroadcast = false});
            controllerInstance.RemoteClientDiscovered += ControllerInstance_RemoteClientDiscovered; 
            ArtNet.Instance.AddInstance(controllerInstance);
        }

        private void ControllerInstance_RemoteClientDiscovered(object sender, RemoteClient e)
        {
            int x = 0;
        }

        public void Write(byte[] dmxData)
        {
            controllerInstance.sendArtDMX(new IPv4Address(new byte[] { 192, 168, 3, 203 }), new org.dmxc.wkdt.Light.ArtNet.PortAddress(_universe), 0, dmxData, 0).Wait();
            //controllerInstance.WriteDMXValues(1, dmxData);
            //controllerInstance.WriteDMXValues(0, dmxData);
        }
    }
}
