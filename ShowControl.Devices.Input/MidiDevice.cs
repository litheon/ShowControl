using System;
using System.Collections.Generic;
using System.Text;
using RtMidi.Core;
using RtMidi.Core.Devices;
using RtMidi.Core.Devices.Infos;
using RtMidi.Core.Messages;
using RtIMidiInputDevice = RtMidi.Core.Devices.IMidiInputDevice;
using RtIMidiOutputDevice = RtMidi.Core.Devices.IMidiOutputDevice;


namespace ShowControl.Devices.Input
{
    public abstract class MidiDevice : IDisposable
    {
        internal RtIMidiInputDevice _midiIn;
        internal IMidiOutputDevice _midiOut;
        private bool _stopped;
        public MidiDevice(IMidiInputDeviceInfo inDeviceNo, IMidiOutputDeviceInfo outDeviceNo)
        {
            _midiIn = inDeviceNo.CreateDevice();
            _midiOut = outDeviceNo.CreateDevice();
            _midiIn.NoteOn += NoteOnMessageHandler;
            _midiIn.NoteOff += NoteOffMessageHandler;
            _midiIn.ControlChange += ControlChangeMessageHandler;
            _midiIn.Open();
            _midiOut.Open();
        }

        public virtual void Reconnect(IMidiInputDeviceInfo inDeviceNo, IMidiOutputDeviceInfo outDeviceNo)
        {
            _midiIn.Dispose();
            _midiOut.Dispose();
            _midiIn = inDeviceNo.CreateDevice();
            _midiOut = outDeviceNo.CreateDevice();
            _midiIn.NoteOn += NoteOnMessageHandler;
            _midiIn.NoteOff += NoteOffMessageHandler;
            _midiIn.ControlChange += ControlChangeMessageHandler;
            _midiIn.Open();
            _midiOut.Open();
        }

        public void Dispose()
        {
            Stop();
        }

        public void Stop()
        {
            if (!_stopped)
            {
                _midiIn.Close();
                _midiIn.Dispose();
                _midiOut.Close();
                _midiOut.Dispose();
                _stopped = true;
            }
        }

        internal abstract void NoteOnMessageHandler(RtIMidiInputDevice sender, in NoteOnMessage msg);

        internal abstract void NoteOffMessageHandler(RtIMidiInputDevice sender, in NoteOffMessage msg);

        internal abstract void ControlChangeMessageHandler(RtIMidiInputDevice sender, in ControlChangeMessage msg);
    }
}
