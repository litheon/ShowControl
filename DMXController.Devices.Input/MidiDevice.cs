using System;
using System.Collections.Generic;
using System.Text;
using NAudio.Midi;

namespace ShowControl.Devices.Input
{
    public abstract class MidiDevice : IDisposable
    {
        internal MidiIn _midiIn;
        internal MidiOut _midiOut;
        private bool _stopped;
        public MidiDevice(int inDeviceNo, int outDeviceNo)
        {
            _midiIn = new MidiIn(inDeviceNo);
            _midiOut = new MidiOut(outDeviceNo);
            _midiIn.MessageReceived += MidiIn_MessageReceived;
        }

        public void Dispose()
        {
            Stop();
        }

        public void Stop()
        {
            if (!_stopped)
            {
                _midiIn.Stop();
                _midiIn.Dispose();
                _midiOut.Dispose();
                _stopped = true;
            }
        }

        internal abstract void MidiIn_MessageReceived(object sender, MidiInMessageEventArgs e);
    }
}
