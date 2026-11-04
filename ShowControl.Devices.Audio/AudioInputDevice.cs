namespace ShowControl.Devices.Audio
{
    /// <summary>One selectable capture device, as the UI sees it.</summary>
    /// <param name="Index">PortAudio device index — stable for the process lifetime
    /// (PortAudio fixes the device list at Initialize, so gear plugged in after launch
    /// appears only after a restart).</param>
    /// <param name="Name">Driver-reported display name.</param>
    /// <param name="ChannelCount">Input channels the device exposes; the capture stream opens
    /// all of them and the channel picker chooses a pair from among them.</param>
    /// <param name="IsActive">Whether this device currently feeds the capture stream.</param>
    public readonly record struct AudioInputDevice(int Index, string Name, int ChannelCount, bool IsActive);
}
