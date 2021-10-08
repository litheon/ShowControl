using NAudio.Midi;
using System;
using System.Collections.Generic;
using System.Text;

namespace ShowControl.Devices.Input
{
    public static class MidiMessageExtensions
    {
        public static byte[] GetMidiMessageAsByteArray(this NoteOnEvent noteOnEvent)
        {
            int message = noteOnEvent.GetAsShortMessage();
            byte byte1 = (byte)((message & 0xff000000) >> 24);
            byte byte2 = (byte)((message & 0x00ff0000) >> 16);
            byte byte3 = (byte)((message & 0x0000ff00) >> 8);
            byte byte4 = (byte)((message & 0x000000ff));
            return new byte[] { byte4, byte3, byte2 };
        }
    }
}
