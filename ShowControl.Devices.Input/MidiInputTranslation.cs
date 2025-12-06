using NAudio.Midi;
using RtMidi.Core.Messages;
using System;
using System.Collections.Generic;
using System.Text;

namespace ShowControl.Devices.Input
{
    public class MidiInputMapping
    {
        public ControlChangeMessage ControlChangeEvent;
        public Func<ControlChangeMessage, DeviceInputEventArgs> DeviceInputEventArgsFunc;
        public MidiInputMapping(ControlChangeMessage controlChangeEvent, Func<ControlChangeMessage, DeviceInputEventArgs> deviceInputEventArgsFunc)
        {
            ControlChangeEvent = controlChangeEvent;
            DeviceInputEventArgsFunc = deviceInputEventArgsFunc;
        }
    }

    public class MidiInputTranslation
    {
        private readonly Dictionary<byte, Dictionary<int, Func<ControlChangeMessage, DeviceInputEventArgs>>> _controlChangeTranslation;
        public MidiInputTranslation(params MidiInputMapping[] midiInputMappings)
        {
            _controlChangeTranslation = new Dictionary<byte, Dictionary<int, Func<ControlChangeMessage, DeviceInputEventArgs>>>();
            foreach (var midiInputMapping in midiInputMappings)
            {
                if (midiInputMapping.DeviceInputEventArgsFunc != null)
                {
                    var controlChangeEvent = midiInputMapping.ControlChangeEvent;
                    var controller = (byte)controlChangeEvent.Control;
                    if (!_controlChangeTranslation.ContainsKey(controller))
                    {
                        _controlChangeTranslation[controller] = new Dictionary<int, Func<ControlChangeMessage, DeviceInputEventArgs>>();
                    }
                    _controlChangeTranslation[controller][controlChangeEvent.Value] = midiInputMapping.DeviceInputEventArgsFunc;
                }
            }
        }

        public DeviceInputEventArgs Translate(ControlChangeMessage controlChangeEvent)
        {
            Func<ControlChangeMessage, DeviceInputEventArgs> returnFunc = null;
            if (_controlChangeTranslation.TryGetValue((byte)controlChangeEvent.Control, out Dictionary<int, Func<ControlChangeMessage, DeviceInputEventArgs>> valueMapping))
            {
                valueMapping.TryGetValue(controlChangeEvent.Value, out returnFunc);
            }
            if (returnFunc != null)
            {
                return returnFunc(controlChangeEvent);
            }
            return null;
        }
    }
}
