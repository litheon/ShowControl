using System;
using RtMidi.Core.Devices.Infos;

namespace ShowControl.Devices.Input
{
    /// <summary>
    /// Stands in for a control surface that is not plugged in.
    /// <para>
    /// The rig has to come up without one: the lights, the shader and the DMX output do not depend
    /// on the surface, and a venue where the ATOM is unplugged (or asleep) must still get a running
    /// show rather than a crash on the way up. Reports no pads, no encoders and no capabilities, so
    /// every caller that asks before it acts naturally does nothing, and swallows the writes from
    /// the callers that do not.
    /// </para>
    /// </summary>
    public sealed class AbsentMidiInputDevice : IMidiInputDevice
    {
        public static readonly AbsentMidiInputDevice Instance = new AbsentMidiInputDevice();

        private AbsentMidiInputDevice()
        {
        }

        public int DeviceId => -1;

        public DeviceType Type => DeviceType.Midi;

        public DeviceCapabilities Capabilities => default;

        public int PadCount => 0;

        public int RotaryEncoderCount => 0;

        /// <summary>Never raised. Kept so the surface can be subscribed to unconditionally.</summary>
        public event EventHandler<DeviceInputEventArgs> OnInputReceived
        {
            add { }
            remove { }
        }

        public void SetPadState(int index, PadState padStates)
        {
        }

        public void SetPadStates(PadState[] padStates)
        {
        }

        public void SetButtonIllumination(Button button, LightState state)
        {
        }

        public void SetButtonIlluminations(ButtonState[] buttonStates)
        {
        }

        public LightState? GetButtonIllumination(Button button) => null;

        public void ClearInterface()
        {
        }

        public void Stop()
        {
        }

        public void Reconnect(IMidiInputDeviceInfo inDeviceNo, IMidiOutputDeviceInfo outDeviceNo)
        {
        }

        public override string ToString() => "(no control surface)";
    }
}
