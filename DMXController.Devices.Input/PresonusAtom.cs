using NAudio.Midi;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Text;

namespace ShowControl.Devices.Input
{
    public class PresonusAtom : MidiDevice, IMidiInputDevice
    {
        public enum PresonusAtomButton { Up=87, Down, Left, Right, Play = 109 };
        private readonly MidiInputTranslation _midiInputTranslation;
        private readonly Dictionary<Button, PresonusAtomButton> _buttonTranslation = new Dictionary<Button, PresonusAtomButton>() {
            {Button.Up, PresonusAtomButton.Up },
            {Button.Play, PresonusAtomButton.Play }
        };
        public PresonusAtom(int deviceId, int midiInDeviceId, int midiOutDeviceId) : base(midiInDeviceId, midiOutDeviceId)
        {
            _midiInputTranslation = new MidiInputTranslation(
                new MidiInputMapping(
                    new ControlChangeEvent(0, 1, (MidiController)PresonusAtomButton.Up, 127),
                    (_) => new DeviceInputEventArgs(this, NavigationButton.Up, ButtonDirection.Press)
                ),
                new MidiInputMapping(
                    new ControlChangeEvent(0, 1, (MidiController)PresonusAtomButton.Up, 0),
                    (_) => new DeviceInputEventArgs(this, NavigationButton.Up, ButtonDirection.Release)
                ),
                new MidiInputMapping(
                    new ControlChangeEvent(0, 1, (MidiController)PresonusAtomButton.Play, 127),
                    (_) => new DeviceInputEventArgs(this, TransportButton.Play, ButtonDirection.Press)
                ),
                new MidiInputMapping(
                    new ControlChangeEvent(0, 1, (MidiController)PresonusAtomButton.Play, 0),
                    (_) => new DeviceInputEventArgs(this, TransportButton.Play, ButtonDirection.Release)
                ),
                new MidiInputMapping(
                    new ControlChangeEvent(0, 1, (MidiController)14, 65),
                    (cce) => new DeviceInputEventArgs(this, RotaryDirection.Down, (byte)cce.Controller, null)
                ),
                new MidiInputMapping(
                    new ControlChangeEvent(0, 1, (MidiController)14, 1),
                    (cce) => new DeviceInputEventArgs(this, RotaryDirection.Up, (byte)cce.Controller, null)
                ),
                new MidiInputMapping(
                    new ControlChangeEvent(0, 1, (MidiController)15, 65),
                    (cce) => new DeviceInputEventArgs(this, RotaryDirection.Down, (byte)cce.Controller, null)
                ),
                new MidiInputMapping(
                    new ControlChangeEvent(0, 1, (MidiController)15, 1),
                    (cce) => new DeviceInputEventArgs(this, RotaryDirection.Up, (byte)cce.Controller, null)
                ),
                new MidiInputMapping(
                    new ControlChangeEvent(0, 1, (MidiController)16, 65),
                    (cce) => new DeviceInputEventArgs(this, RotaryDirection.Down, (byte)cce.Controller, null)
                ),
                new MidiInputMapping(
                    new ControlChangeEvent(0, 1, (MidiController)16, 1),
                    (cce) => new DeviceInputEventArgs(this, RotaryDirection.Up, (byte)cce.Controller, null)
                ),
                new MidiInputMapping(
                    new ControlChangeEvent(0, 1, (MidiController)17, 65),
                    (cce) => new DeviceInputEventArgs(this, RotaryDirection.Down, (byte)cce.Controller, null)
                ),
                new MidiInputMapping(
                    new ControlChangeEvent(0, 1, (MidiController)17, 1),
                    (cce) => new DeviceInputEventArgs(this, RotaryDirection.Up, (byte)cce.Controller, null)
                )
            );

            DeviceId = deviceId;
            var advancedControlModeSignal = new NoteEvent(0, channel: 16, commandCode: MidiCommandCode.NoteOff, noteNumber: 0, velocity: 127);
            _midiOut.Send(advancedControlModeSignal.GetAsShortMessage());
            _midiIn.Start();
            ClearInterface();
        }

        public int DeviceId { get; private set; }

        public DeviceType Type => DeviceType.Midi;

        public DeviceCapabilities Capabilities =>
            DeviceCapabilities.Navigation |
            DeviceCapabilities.Pads |
            DeviceCapabilities.RotaryEncoders |
            DeviceCapabilities.Transport |
            DeviceCapabilities.ButtonIllumination;

        public int PadCount => 16;

        public int RotaryEncoderCount => 4;

        public event EventHandler<DeviceInputEventArgs> OnInputReceived;

        public void ClearInterface()
        {
            for (int i = 0; i < PadCount; i++)
            {
                SetPadColor(i, Color.FromArgb(0x00, 0x00, 0x00, 0x00));
            }
            _midiOut.Send(new ControlChangeEvent(absoluteTime: 0, channel: 1, controller: (MidiController)PresonusAtomButton.Up, 0).GetAsShortMessage());
            _midiOut.Send(new ControlChangeEvent(absoluteTime: 0, channel: 1, controller: (MidiController)PresonusAtomButton.Play, 0).GetAsShortMessage());
        }

        public void SetPadColor(int index, Color color)
        {
            if (index >= PadCount)
            {
                throw new ArgumentOutOfRangeException("Pad index is out of range");
            }
            int padNote = index + 36;
            int red = Convert.ToInt32(((double)color.R) / 255 * 127);
            int green = Convert.ToInt32(((double)color.G) / 255 * 127);
            int blue = Convert.ToInt32(((double)color.B) / 255 * 127);
            _midiOut.Send(new NoteOnEvent(absoluteTime: 0, channel: 1, noteNumber: padNote, velocity: 2, duration: 0).GetAsShortMessage());
            _midiOut.Send(new NoteOnEvent(absoluteTime: 0, channel: 2, noteNumber: padNote, velocity: red, duration: 0).GetAsShortMessage());
            _midiOut.Send(new NoteOnEvent(absoluteTime: 0, channel: 3, noteNumber: padNote, velocity: green, duration: 0).GetAsShortMessage());
            _midiOut.Send(new NoteOnEvent(absoluteTime: 0, channel: 4, noteNumber: padNote, velocity: blue, duration: 0).GetAsShortMessage());
        }

        internal override void MidiIn_MessageReceived(object sender, MidiInMessageEventArgs e)
        {
            if (e.MidiEvent.CommandCode == MidiCommandCode.ControlChange)
            {
                var controlChangeEvent = e.MidiEvent as ControlChangeEvent;
                var eventArgs = _midiInputTranslation.Translate(controlChangeEvent);
                if (eventArgs != null)
                {
                    OnInputReceived?.Invoke(this, eventArgs);
                }
            }
            else if (e.MidiEvent.CommandCode == MidiCommandCode.NoteOn)
            {
                var noteOnEvent = e.MidiEvent as NoteOnEvent;
                if (noteOnEvent.NoteNumber >= 36 && noteOnEvent.NoteNumber <= 52)
                {
                    // Pad press event
                    byte padIndex = (byte)(noteOnEvent.NoteNumber - 36);
                    OnInputReceived?.Invoke(this, new DeviceInputEventArgs(this, ButtonDirection.Press, padIndex));
                }
            }
            else if (e.MidiEvent.CommandCode == MidiCommandCode.NoteOff)
            {
                var noteOnEvent = e.MidiEvent as NoteEvent;
                if (noteOnEvent.NoteNumber >= 36 && noteOnEvent.NoteNumber <= 52)
                {
                    // Pad press event
                    byte padIndex = (byte)(noteOnEvent.NoteNumber - 36);
                    OnInputReceived?.Invoke(this, new DeviceInputEventArgs(this, ButtonDirection.Release, padIndex));
                }
            }
        }

        public void SetButtonIllumination(Input.Button button, LightState state)
        {
            if (_buttonTranslation.ContainsKey(button))
            {
                _midiOut.Send(new ControlChangeEvent(absoluteTime: 0, channel: 1, controller: (MidiController)_buttonTranslation[button], state == LightState.On ? 127 : 0).GetAsShortMessage());
            }
        }
    }
}
