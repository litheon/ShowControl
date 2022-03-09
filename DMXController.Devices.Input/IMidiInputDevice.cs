using System;

namespace ShowControl.Devices.Input
{
    public enum Button
    {
        Up,
        Down,
        Left,
        Right,
        Select,
        Play,
        Stop
    };

    public interface IReadOnlyMidiInputDevice
    {
        public int DeviceId { get; }
        public DeviceType Type { get; }
        public DeviceCapabilities Capabilities { get; }
        public int PadCount { get; }
        public int RotaryEncoderCount { get; }
    }

    public interface IMidiInputDevice : IReadOnlyMidiInputDevice
    {
        public void SetPadColor(int index, System.Drawing.Color color);
        public void SetPadColors(System.Drawing.Color[] color);
        public void SetButtonIllumination(Button button, LightState state);
        public void ClearInterface();
        public void Stop();
        public event EventHandler<DeviceInputEventArgs> OnInputReceived;
    }
}
