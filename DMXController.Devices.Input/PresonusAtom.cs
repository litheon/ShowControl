using NAudio.Midi;
using RtMidi.Core.Devices.Infos;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Text;
using System.Threading;
using RtMidi.Core.Devices;
using RtIMidiInputDevice = RtMidi.Core.Devices.IMidiInputDevice;
using RtIMidiOutputDevice = RtMidi.Core.Devices.IMidiOutputDevice;
using RtMidi.Core.Messages;

namespace ShowControl.Devices.Input
{
    public class PresonusAtom : MidiDevice, IMidiInputDevice
    {
        public enum PresonusAtomButton : byte { Up = 87, Down = 89, Left = 90, Right = 102, Select = 103, Zoom = 104, Play = 109 };
        private enum PresonusAtomPadIlluminationType { Off, Blink, Breathe, Solid = 127 };
        private readonly MidiInputTranslation _midiInputTranslation;
        private readonly Dictionary<Button, PresonusAtomButton> _buttonTranslation = new Dictionary<Button, PresonusAtomButton>() {
            {Button.Up, PresonusAtomButton.Up },
            {Button.Play, PresonusAtomButton.Play },
            {Button.Down, PresonusAtomButton.Down },
            {Button.Left, PresonusAtomButton.Left },
            {Button.Right, PresonusAtomButton.Right },
            {Button.Select, PresonusAtomButton.Select },
            {Button.Zoom, PresonusAtomButton.Zoom },
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
        public PresonusAtom(int deviceId, IMidiInputDeviceInfo midiInDeviceInfo, IMidiOutputDeviceInfo midiOutDeviceInfo) : base(midiInDeviceInfo, midiOutDeviceInfo)
        {
            _midiInputTranslation = new MidiInputTranslation(
                new MidiInputMapping(
                    new ControlChangeMessage((RtMidi.Core.Enums.Channel)1, (int)PresonusAtomButton.Up, ButtonPressControllerValue),
                    (_) => new DeviceInputEventArgs(this, NavigationButton.Up, ButtonDirection.Press)
                ),
                new MidiInputMapping(
                    new ControlChangeMessage((RtMidi.Core.Enums.Channel)1, (int)PresonusAtomButton.Up, ButtonReleaseControllerValue),
                    (_) => new DeviceInputEventArgs(this, NavigationButton.Up, ButtonDirection.Release)
                ),
                new MidiInputMapping(
                    new ControlChangeMessage((RtMidi.Core.Enums.Channel)1, (int)PresonusAtomButton.Down, ButtonPressControllerValue),
                    (_) => new DeviceInputEventArgs(this, NavigationButton.Down, ButtonDirection.Press)
                ),
                new MidiInputMapping(
                    new ControlChangeMessage((RtMidi.Core.Enums.Channel)1, (int)PresonusAtomButton.Down, ButtonReleaseControllerValue),
                    (_) => new DeviceInputEventArgs(this, NavigationButton.Down, ButtonDirection.Release)
                ),
                new MidiInputMapping(
                    new ControlChangeMessage((RtMidi.Core.Enums.Channel)1, (int)PresonusAtomButton.Left, ButtonPressControllerValue),
                    (_) => new DeviceInputEventArgs(this, NavigationButton.Left, ButtonDirection.Press)
                ),
                new MidiInputMapping(
                    new ControlChangeMessage((RtMidi.Core.Enums.Channel)1, (int)PresonusAtomButton.Left, ButtonReleaseControllerValue),
                    (_) => new DeviceInputEventArgs(this, NavigationButton.Left, ButtonDirection.Release)
                ),
                new MidiInputMapping(
                    new ControlChangeMessage((RtMidi.Core.Enums.Channel)1, (int)PresonusAtomButton.Right, ButtonPressControllerValue),
                    (_) => new DeviceInputEventArgs(this, NavigationButton.Right, ButtonDirection.Press)
                ),
                new MidiInputMapping(
                    new ControlChangeMessage((RtMidi.Core.Enums.Channel)1, (int)PresonusAtomButton.Right, ButtonReleaseControllerValue),
                    (_) => new DeviceInputEventArgs(this, NavigationButton.Right, ButtonDirection.Release)
                ),
                new MidiInputMapping(
                    new ControlChangeMessage((RtMidi.Core.Enums.Channel)1, (int)PresonusAtomButton.Select, ButtonPressControllerValue),
                    (_) => new DeviceInputEventArgs(this, NavigationButton.Select, ButtonDirection.Press)
                ),
                new MidiInputMapping(
                    new ControlChangeMessage((RtMidi.Core.Enums.Channel)1, (int)PresonusAtomButton.Select, ButtonReleaseControllerValue),
                    (_) => new DeviceInputEventArgs(this, NavigationButton.Select, ButtonDirection.Release)
                ),
                new MidiInputMapping(
                    new ControlChangeMessage((RtMidi.Core.Enums.Channel)1, (int)PresonusAtomButton.Zoom, ButtonPressControllerValue),
                    (_) => new DeviceInputEventArgs(this, NavigationButton.Zoom, ButtonDirection.Press)
                ),
                new MidiInputMapping(
                    new ControlChangeMessage((RtMidi.Core.Enums.Channel)1, (int)PresonusAtomButton.Zoom, ButtonReleaseControllerValue),
                    (_) => new DeviceInputEventArgs(this, NavigationButton.Zoom, ButtonDirection.Release)
                ),
                new MidiInputMapping(
                    new ControlChangeMessage((RtMidi.Core.Enums.Channel)1, (int)PresonusAtomButton.Play, ButtonPressControllerValue),
                    (_) => new DeviceInputEventArgs(this, TransportButton.Play, ButtonDirection.Press)
                ),
                new MidiInputMapping(
                    new ControlChangeMessage((RtMidi.Core.Enums.Channel)1, (int)PresonusAtomButton.Play, ButtonReleaseControllerValue),
                    (_) => new DeviceInputEventArgs(this, TransportButton.Play, ButtonDirection.Release)
                ),
                new MidiInputMapping(
                    new ControlChangeMessage((RtMidi.Core.Enums.Channel)1, RotaryEncoder1, RotaryDownControllerValue),
                    (cce) => new DeviceInputEventArgs(this, RotaryDirection.Down, (byte)((byte)cce.Control - RotaryEncoderOffset), null)
                ),
                new MidiInputMapping(
                    new ControlChangeMessage((RtMidi.Core.Enums.Channel)1, RotaryEncoder1, RotaryUpControllerValue),
                    (cce) => new DeviceInputEventArgs(this, RotaryDirection.Up, (byte)((byte)cce.Control - RotaryEncoderOffset), null)
                ),
                new MidiInputMapping(
                    new ControlChangeMessage((RtMidi.Core.Enums.Channel)1, RotaryEncoder2, RotaryDownControllerValue),
                    (cce) => new DeviceInputEventArgs(this, RotaryDirection.Down, (byte)((byte)cce.Control - RotaryEncoderOffset), null)
                ),
                new MidiInputMapping(
                    new ControlChangeMessage((RtMidi.Core.Enums.Channel)1, RotaryEncoder2, RotaryUpControllerValue),
                    (cce) => new DeviceInputEventArgs(this, RotaryDirection.Up, (byte)((byte)cce.Control - RotaryEncoderOffset), null)
                ),
                new MidiInputMapping(
                    new ControlChangeMessage((RtMidi.Core.Enums.Channel)1, RotaryEncoder3, RotaryDownControllerValue),
                    (cce) => new DeviceInputEventArgs(this, RotaryDirection.Down, (byte)((byte)cce.Control - RotaryEncoderOffset), null)
                ),
                new MidiInputMapping(
                    new ControlChangeMessage((RtMidi.Core.Enums.Channel)1, RotaryEncoder3, RotaryUpControllerValue),
                    (cce) => new DeviceInputEventArgs(this, RotaryDirection.Up, (byte)((byte)cce.Control - RotaryEncoderOffset), null)
                ),
                new MidiInputMapping(
                    new ControlChangeMessage((RtMidi.Core.Enums.Channel)1, RotaryEncoder4, RotaryDownControllerValue),
                    (cce) => new DeviceInputEventArgs(this, RotaryDirection.Down, (byte)((byte)cce.Control - RotaryEncoderOffset), null)
                ),
                new MidiInputMapping(
                    new ControlChangeMessage((RtMidi.Core.Enums.Channel)1, RotaryEncoder4, RotaryUpControllerValue),
                    (cce) => new DeviceInputEventArgs(this, RotaryDirection.Up, (byte)((byte)cce.Control - RotaryEncoderOffset), null)
                )
            );

            DeviceId = deviceId;
            InitializeInterface();
        }

        private void InitializeInterface()
        {
            var advancedControlModeSignal = new NoteEvent(0, channel: 16, commandCode: MidiCommandCode.NoteOff, noteNumber: 0, velocity: 127);
            //_midiOut.Send(advancedControlModeSignal.GetAsShortMessage());
            _midiOut.Send(new RtMidi.Core.Messages.NoteOffMessage(RtMidi.Core.Enums.Channel.Channel16, RtMidi.Core.Enums.Key.Key0, velocity: 127));
        }

        public override void Reconnect(IMidiInputDeviceInfo inDeviceNo, IMidiOutputDeviceInfo outDeviceNo)
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
            List<byte> bytes = new List<byte>();
            foreach (var item in Enum.GetValues(typeof(PresonusAtomButton)))
            {
                bytes.AddRange(new ControlChangeEvent(absoluteTime: 0, channel: 1, controller: (MidiController)(item), 0).GetMidiMessageAsByteArray());
            }
            _midiOut.Send(bytes.ToArray());
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
            _midiOut.Send(bytes.ToArray());
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
            _midiOut.Send(bytes.ToArray());
        }

        internal void MidiIn_MessageReceived(object sender, MidiInMessageEventArgs e)
        {

        }

        internal override void NoteOnMessageHandler(RtIMidiInputDevice sender, in NoteOnMessage msg)
        {
            if ((int)msg.Key >= FirstPadNoteNumber && (int)msg.Key <= LastPadNoteNumber)
            {
                // Pad press event
                byte padIndex = (byte)((int)msg.Key - FirstPadNoteNumber);
                OnInputReceived?.Invoke(this, new DeviceInputEventArgs(this, ButtonDirection.Press, padIndex, msg.Velocity));
            }
        }

        internal override void NoteOffMessageHandler(RtIMidiInputDevice sender, in NoteOffMessage msg)
        {
            if ((int)msg.Key >= FirstPadNoteNumber && (int)msg.Key <= LastPadNoteNumber)
            {
                // Pad press event
                byte padIndex = (byte)((int)msg.Key - FirstPadNoteNumber);
                OnInputReceived?.Invoke(this, new DeviceInputEventArgs(this, ButtonDirection.Release, padIndex, msg.Velocity));
            }
        }

        internal override void ControlChangeMessageHandler(RtIMidiInputDevice sender, in ControlChangeMessage msg)
        {
            var eventArgs = _midiInputTranslation.Translate(msg);
            if (eventArgs != null)
            {
                OnInputReceived?.Invoke(this, eventArgs);
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
                _midiOut.Send(new ControlChangeEvent(absoluteTime: 0, channel: 1, controller: (MidiController)_buttonTranslation[button], state == LightState.On ? (int)PresonusAtomPadIlluminationType.Solid : 0).GetMidiMessageAsByteArray());
            }
        }

        public void SetButtonIlluminations(ButtonState[] buttonStates)
        {
            List<byte> bytes = new List<byte>();
            foreach (var  buttonState in buttonStates)
            {
                if (_buttonTranslation.ContainsKey(buttonState.Button))
                {
                    _buttonIllumination[buttonState.Button] = buttonState.LightState;
                    bytes.AddRange(new ControlChangeEvent(absoluteTime: 0, channel: 1, controller: (MidiController)_buttonTranslation[buttonState.Button], buttonState.LightState == LightState.On ? (int)PresonusAtomPadIlluminationType.Solid : 0).GetMidiMessageAsByteArray());
                }
            }
            if (bytes.Count > 0)
            {
                _midiOut.Send(bytes.ToArray());
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
