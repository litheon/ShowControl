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
        Stop,
        Zoom
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
        public void SetPadState(int index, PadState padStates);
        public void SetPadStates(PadState[] padStates);
        public void SetButtonIllumination(Button button, LightState state);
        public void SetButtonIlluminations(ButtonState[] buttonStates);
        public LightState? GetButtonIllumination(Button button);
        public void ClearInterface();
        public void Stop();
        public void Reconnect(int inDeviceNo, int outDeviceNo);
        public event EventHandler<DeviceInputEventArgs> OnInputReceived;
    }
}
