using System;
using System.Collections.Generic;
using System.Text;
using NAudio.Midi;

namespace ShowControl.Devices.Input
{
    public abstract class MidiDevice
    {
        internal MidiIn _midiIn;
        internal MidiOut _midiOut;
        public MidiDevice(int inDeviceNo, int outDeviceNo)
        {
            _midiIn = new MidiIn(inDeviceNo);
            _midiOut = new MidiOut(outDeviceNo);
            _midiIn.MessageReceived += MidiIn_MessageReceived;
        }

        internal abstract void MidiIn_MessageReceived(object sender, MidiInMessageEventArgs e);
    }
}
