using System;
using System.Collections.Generic;
using System.Text;

namespace ShowControl.Devices.Input
{
    public interface IMidiDeviceEnumerator
    {
        public MidiInOutDevice[] GetMidiDevices();
    }
}
