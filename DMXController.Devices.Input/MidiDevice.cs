using System;
using System.Collections.Generic;
using System.Text;
using RtMidi.Core;
using RtMidi.Core.Devices;
using RtMidi.Core.Devices.Infos;
using RtMidi.Core.Messages;
using RtIMidiInputDevice = RtMidi.Core.Devices.IMidiInputDevice;
using RtIMidiOutputDevice = RtMidi.Core.Devices.IMidiOutputDevice;
using NAudio.Midi;
using System.Collections;


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
            _midiIn.Open();
            _midiOut.Open();
        }

        public virtual void Reconnect(IMidiInputDeviceInfo inDeviceNo, IMidiOutputDeviceInfo outDeviceNo)
        {
            _midiIn.Dispose();
            _midiOut.Dispose();
            _midiIn = inDeviceNo.CreateDevice();
            _midiOut = outDeviceNo.CreateDevice();
            _midiIn.Open();
            _midiOut.Open();
        }

        private void _midiIn_ErrorReceived(object sender, MidiInMessageEventArgs e)
        {
            int x = 0;
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

        private void NoteOnMessageHandler(RtIMidiInputDevice sender, in NoteOnMessage msg)
        {
            var val = ((0x09 & 0xFF) << 4) | (((int)msg.Channel + 1) & 0xFF) << 4 | (((int)msg.Key + 1) & 0xFF) << 8 | (msg.Velocity & 0xFF) << 16;
            MidiIn_MessageReceived(sender, new MidiInMessageEventArgs(val, 0));
        } 

        internal abstract void MidiIn_MessageReceived(object sender, MidiInMessageEventArgs e);
    }
}
