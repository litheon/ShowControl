using NAudio.Midi;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Text;
using System.Threading;

namespace ShowControl.Devices.Input
{
    public class PresonusAtom : MidiDevice, IMidiInputDevice
    {
        public enum PresonusAtomButton { Up = 87, Down, Left, Right, Play = 109 };
        private enum PresonusAtomPadIlluminationType { Off, Blink, Breathe, Solid = 127 };
        private readonly MidiInputTranslation _midiInputTranslation;
        private readonly Dictionary<Button, PresonusAtomButton> _buttonTranslation = new Dictionary<Button, PresonusAtomButton>() {
            {Button.Up, PresonusAtomButton.Up },
            {Button.Play, PresonusAtomButton.Play }
        };
        private readonly Dictionary<Button, LightState> _buttonIllumination = new Dictionary<Button, LightState>();
        private const int RotaryEncoderOffset = 13;
        private const int RotaryEncoder1 = 14;
        private const int RotaryEncoder2 = 15;
        private const int RotaryEncoder3 = 16;
        private const int RotaryEncoder4 = 17;

        private const int RotaryUpControllerValue = 1;
        private const int RotaryDownControllerValue = 65;

        private const int ButtonPressControllerValue = 127;
        private const int ButtonReleaseControllerValue = 0;

        private const int FirstPadNoteNumber = 36;
        private const int LastPadNoteNumber = 52;
        public PresonusAtom(int deviceId, int midiInDeviceId, int midiOutDeviceId) : base(midiInDeviceId, midiOutDeviceId)
        {
            _midiInputTranslation = new MidiInputTranslation(
                new MidiInputMapping(
                    new ControlChangeEvent(0, 1, (MidiController)PresonusAtomButton.Up, ButtonPressControllerValue),
                    (_) => new DeviceInputEventArgs(this, NavigationButton.Up, ButtonDirection.Press)
                ),
                new MidiInputMapping(
                    new ControlChangeEvent(0, 1, (MidiController)PresonusAtomButton.Up, ButtonReleaseControllerValue),
                    (_) => new DeviceInputEventArgs(this, NavigationButton.Up, ButtonDirection.Release)
                ),
                new MidiInputMapping(
                    new ControlChangeEvent(0, 1, (MidiController)PresonusAtomButton.Play, ButtonPressControllerValue),
                    (_) => new DeviceInputEventArgs(this, TransportButton.Play, ButtonDirection.Press)
                ),
                new MidiInputMapping(
                    new ControlChangeEvent(0, 1, (MidiController)PresonusAtomButton.Play, ButtonReleaseControllerValue),
                    (_) => new DeviceInputEventArgs(this, TransportButton.Play, ButtonDirection.Release)
                ),
                new MidiInputMapping(
                    new ControlChangeEvent(0, 1, (MidiController)RotaryEncoder1, RotaryDownControllerValue),
                    (cce) => new DeviceInputEventArgs(this, RotaryDirection.Down, (byte)((byte)cce.Controller - RotaryEncoderOffset), null)
                ),
                new MidiInputMapping(
                    new ControlChangeEvent(0, 1, (MidiController)RotaryEncoder1, RotaryUpControllerValue),
                    (cce) => new DeviceInputEventArgs(this, RotaryDirection.Up, (byte)((byte)cce.Controller - RotaryEncoderOffset), null)
                ),
                new MidiInputMapping(
                    new ControlChangeEvent(0, 1, (MidiController)RotaryEncoder2, RotaryDownControllerValue),
                    (cce) => new DeviceInputEventArgs(this, RotaryDirection.Down, (byte)((byte)cce.Controller - RotaryEncoderOffset), null)
                ),
                new MidiInputMapping(
                    new ControlChangeEvent(0, 1, (MidiController)RotaryEncoder2, RotaryUpControllerValue),
                    (cce) => new DeviceInputEventArgs(this, RotaryDirection.Up, (byte)((byte)cce.Controller - RotaryEncoderOffset), null)
                ),
                new MidiInputMapping(
                    new ControlChangeEvent(0, 1, (MidiController)RotaryEncoder3, RotaryDownControllerValue),
                    (cce) => new DeviceInputEventArgs(this, RotaryDirection.Down, (byte)((byte)cce.Controller - RotaryEncoderOffset), null)
                ),
                new MidiInputMapping(
                    new ControlChangeEvent(0, 1, (MidiController)RotaryEncoder3, RotaryUpControllerValue),
                    (cce) => new DeviceInputEventArgs(this, RotaryDirection.Up, (byte)((byte)cce.Controller - RotaryEncoderOffset), null)
                ),
                new MidiInputMapping(
                    new ControlChangeEvent(0, 1, (MidiController)RotaryEncoder4, RotaryDownControllerValue),
                    (cce) => new DeviceInputEventArgs(this, RotaryDirection.Down, (byte)((byte)cce.Controller - RotaryEncoderOffset), null)
                ),
                new MidiInputMapping(
                    new ControlChangeEvent(0, 1, (MidiController)RotaryEncoder4, RotaryUpControllerValue),
                    (cce) => new DeviceInputEventArgs(this, RotaryDirection.Up, (byte)((byte)cce.Controller - RotaryEncoderOffset), null)
                )
            );

            DeviceId = deviceId;
            InitializeInterface();
        }

        private void InitializeInterface()
        {
            var advancedControlModeSignal = new NoteEvent(0, channel: 16, commandCode: MidiCommandCode.NoteOff, noteNumber: 0, velocity: 127);
            _midiOut.Send(advancedControlModeSignal.GetAsShortMessage());
            _midiIn.Start();
        }

        public override void Reconnect(int inDeviceNo, int outDeviceNo)
        {
            base.Reconnect(inDeviceNo, outDeviceNo);
            InitializeInterface();
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
            var padStates = new PadState[PadCount];
            for (int i = 0; i < padStates.Length; i++)
            {
                padStates[i].Color = Color.FromArgb(0x00, 0x00, 0x00, 0x00);
            }
            SetPadStates(padStates);
            _midiOut.Send(new ControlChangeEvent(absoluteTime: 0, channel: 1, controller: (MidiController)PresonusAtomButton.Up, 0).GetAsShortMessage());
            _midiOut.Send(new ControlChangeEvent(absoluteTime: 0, channel: 1, controller: (MidiController)PresonusAtomButton.Play, 0).GetAsShortMessage());
        }

        public void SetPadState(int index, PadState padState)
        {
            if (index >= PadCount)
            {
                throw new ArgumentOutOfRangeException("Pad index is out of range");
            }
            var color = padState.Color;
            var illumination = ConvertIlluminationType(padState.PadIllumination);
            int padNote = index + FirstPadNoteNumber;
            int red = Convert.ToInt32(((double)color.R) / 255 * 127);
            int green = Convert.ToInt32(((double)color.G) / 255 * 127);
            int blue = Convert.ToInt32(((double)color.B) / 255 * 127);
            List<byte> bytes = new List<byte>();
            bytes.AddRange(new NoteOnEvent(absoluteTime: 0, channel: 1, noteNumber: padNote, velocity: (int)illumination, duration: 0).GetMidiMessageAsByteArray());
            bytes.AddRange(new NoteOnEvent(absoluteTime: 0, channel: 2, noteNumber: padNote, velocity: red, duration: 0).GetMidiMessageAsByteArray());
            bytes.AddRange(new NoteOnEvent(absoluteTime: 0, channel: 3, noteNumber: padNote, velocity: green, duration: 0).GetMidiMessageAsByteArray());
            bytes.AddRange(new NoteOnEvent(absoluteTime: 0, channel: 4, noteNumber: padNote, velocity: blue, duration: 0).GetMidiMessageAsByteArray());
            _midiOut.SendBuffer(bytes.ToArray());
        }

        public void SetPadStates(PadState[] states)
        {
            if (states.Length != PadCount)
            {
                throw new ArgumentOutOfRangeException("Pad index is out of range");
            }
            List<byte> bytes = new List<byte>();
            for (int i = 0; i < states.Length; i++)
            {
                int padNote = i + FirstPadNoteNumber;
                var color = states[i].Color;
                var illumination = ConvertIlluminationType(states[i].PadIllumination);
                int red = Convert.ToInt32(((double)color.R) / 255 * 127);
                int green = Convert.ToInt32(((double)color.G) / 255 * 127);
                int blue = Convert.ToInt32(((double)color.B) / 255 * 127);

                bytes.AddRange(new NoteOnEvent(absoluteTime: 0, channel: 1, noteNumber: padNote, velocity: (int)illumination, duration: 0).GetMidiMessageAsByteArray());
                bytes.AddRange(new NoteOnEvent(absoluteTime: 0, channel: 2, noteNumber: padNote, velocity: red, duration: 0).GetMidiMessageAsByteArray());
                bytes.AddRange(new NoteOnEvent(absoluteTime: 0, channel: 3, noteNumber: padNote, velocity: green, duration: 0).GetMidiMessageAsByteArray());
                bytes.AddRange(new NoteOnEvent(absoluteTime: 0, channel: 4, noteNumber: padNote, velocity: blue, duration: 0).GetMidiMessageAsByteArray());
            }
            _midiOut.SendBuffer(bytes.ToArray());
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
                if (noteOnEvent.NoteNumber >= FirstPadNoteNumber && noteOnEvent.NoteNumber <= LastPadNoteNumber)
                {
                    // Pad press event
                    byte padIndex = (byte)(noteOnEvent.NoteNumber - FirstPadNoteNumber);
                    OnInputReceived?.Invoke(this, new DeviceInputEventArgs(this, ButtonDirection.Press, padIndex, noteOnEvent.Velocity));
                }
            }
            else if (e.MidiEvent.CommandCode == MidiCommandCode.NoteOff)
            {
                var noteOnEvent = e.MidiEvent as NoteEvent;
                if (noteOnEvent.NoteNumber >= FirstPadNoteNumber && noteOnEvent.NoteNumber <= LastPadNoteNumber)
                {
                    // Pad press event
                    byte padIndex = (byte)(noteOnEvent.NoteNumber - FirstPadNoteNumber);
                    OnInputReceived?.Invoke(this, new DeviceInputEventArgs(this, ButtonDirection.Release, padIndex, noteOnEvent.Velocity));
                }
            }
        }

        public LightState? GetButtonIllumination(Input.Button button)
        {
            LightState state;
            if (!_buttonIllumination.TryGetValue(button, out state))
            {
                return null;
            }
            return state;
        }

        public void SetButtonIllumination(Input.Button button, LightState state)
        {
            if (_buttonTranslation.ContainsKey(button))
            {
                _buttonIllumination[button] = state;
                _midiOut.Send(new ControlChangeEvent(absoluteTime: 0, channel: 1, controller: (MidiController)_buttonTranslation[button], state == LightState.On ? (int)PresonusAtomPadIlluminationType.Solid : 0).GetAsShortMessage());
            }
        }

        private PresonusAtomPadIlluminationType ConvertIlluminationType(PadIllumination illumination)
        {
            switch(illumination)
            {
                case PadIllumination.Blink:
                    return PresonusAtomPadIlluminationType.Blink;
                case PadIllumination.Breathe:
                    return PresonusAtomPadIlluminationType.Breathe;
                case PadIllumination.Off:
                    return PresonusAtomPadIlluminationType.Off;
                default:
                case PadIllumination.Solid:
                    return PresonusAtomPadIlluminationType.Solid;
            }
        }
    }
}
