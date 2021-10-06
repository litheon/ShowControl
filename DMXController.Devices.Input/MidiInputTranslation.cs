using NAudio.Midi;
using System;
using System.Collections.Generic;
using System.Text;

namespace ShowControl.Devices.Input
{
    public class MidiInputMapping
    {
        public ControlChangeEvent ControlChangeEvent;
        public Func<ControlChangeEvent, DeviceInputEventArgs> DeviceInputEventArgsFunc;
        public MidiInputMapping(ControlChangeEvent controlChangeEvent, Func<ControlChangeEvent, DeviceInputEventArgs> deviceInputEventArgsFunc)
        {
            ControlChangeEvent = controlChangeEvent;
            DeviceInputEventArgsFunc = deviceInputEventArgsFunc;
        }
    }

    public class MidiInputTranslation
    {
        private Dictionary<byte, Dictionary<int, Func<ControlChangeEvent, DeviceInputEventArgs>>> _controlChangeTranslation;
        public MidiInputTranslation(params MidiInputMapping[] midiInputMappings)
        {
            _controlChangeTranslation = new Dictionary<byte, Dictionary<int, Func<ControlChangeEvent, DeviceInputEventArgs>>>();
            foreach (var midiInputMapping in midiInputMappings)
            {
                if (midiInputMapping.ControlChangeEvent != null && midiInputMapping.DeviceInputEventArgsFunc != null)
                {
                    var controlChangeEvent = midiInputMapping.ControlChangeEvent;
                    var controller = (byte)controlChangeEvent.Controller;
                    if (!_controlChangeTranslation.ContainsKey(controller))
                    {
                        _controlChangeTranslation[controller] = new Dictionary<int, Func<ControlChangeEvent, DeviceInputEventArgs>>();
                    }
                    _controlChangeTranslation[controller][controlChangeEvent.ControllerValue] = midiInputMapping.DeviceInputEventArgsFunc;
                }
            }
        }

        public DeviceInputEventArgs Translate(ControlChangeEvent controlChangeEvent)
        {
            Func<ControlChangeEvent, DeviceInputEventArgs> returnFunc = null;
            Dictionary<int, Func<ControlChangeEvent, DeviceInputEventArgs>> valueMapping = null;
            if (_controlChangeTranslation.TryGetValue((byte)controlChangeEvent.Controller, out valueMapping))
            {
                valueMapping.TryGetValue(controlChangeEvent.ControllerValue, out returnFunc);
            }
            if (returnFunc != null)
            {
                return returnFunc(controlChangeEvent);
            }
            return null;
        }
    }
}
