using System.Threading.Tasks;

namespace ShowControl.Devices.Output
{
    public interface IArtNetController
    {
        Task SendArtDMX(byte[] ipAddress, byte universe, byte sourcePort, byte[] data, byte sequence);
        Task SendSync();
    }
}
