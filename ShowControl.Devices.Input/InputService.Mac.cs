using System;
using System.Collections.Generic;
using System.Text;
using System.Runtime.InteropServices;
using System.Linq;
using ObjCRuntime;

namespace ShowControl.Devices.Input
{
    #if MACCATALYST
    public partial class InputService
    {
        IntPtr midiClient;
        GCHandle _selfHandle;

        static MIDINotifyProc notifyCallback = MidiNotifyStatic;

        public enum MIDIMessageID : uint
        {
            SetupChanged = 1,
            ObjectAdded = 2,
            ObjectRemoved = 3,
            PropertyChanged = 4,
            ThruConnectionsChanged = 5,
            SerialPortOwnerChanged = 6,
            IOError = 7
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct MIDINotification
        {
            public MIDIMessageID MessageID;
            public uint MessageSize;
            // Additional data follows, but not needed here.
        }

        public delegate void MIDINotifyProc(ref MIDINotification notification, IntPtr refCon);

        [DllImport("/System/Library/Frameworks/CoreMIDI.framework/CoreMIDI")]
        public static extern int MIDIClientCreate(
            IntPtr name,
            MIDINotifyProc notifyProc,
            IntPtr notifyRefCon,
            out IntPtr client);

        // Helper to create NSString from C# string (needed for name param)
        [DllImport("/System/Library/Frameworks/Foundation.framework/Foundation")]
        public static extern IntPtr objc_getClass(string className);

        [DllImport("/System/Library/Frameworks/Foundation.framework/Foundation")]
        public static extern IntPtr sel_registerName(string selectorName);

        [DllImport("/System/Library/Frameworks/Foundation.framework/Foundation")]
        public static extern IntPtr objc_msgSend(IntPtr receiver, IntPtr selector);

        [DllImport("/System/Library/Frameworks/Foundation.framework/Foundation")]
        public static extern IntPtr objc_msgSend(IntPtr receiver, IntPtr selector, IntPtr arg);

        public static IntPtr GetNSString(string str)
        {
            var nsString = objc_getClass("NSString");
            var alloc = sel_registerName("alloc");
            var initWithUTF8 = sel_registerName("initWithUTF8String:");

            IntPtr utf8Str = Marshal.StringToHGlobalAnsi(str);
            IntPtr stringPtr = objc_msgSend(nsString, alloc);
            stringPtr = objc_msgSend(stringPtr, initWithUTF8, utf8Str);

            return stringPtr;
        }

        private void PlatformInitialization()
        {
            IntPtr namePtr = GetNSString("MAUI MidiClient");

            // Allocate a GCHandle to pass 'this' through the refCon parameter
            _selfHandle = GCHandle.Alloc(this);

            int result = MIDIClientCreate(namePtr, notifyCallback, GCHandle.ToIntPtr(_selfHandle), out midiClient);
            Marshal.FreeHGlobal(namePtr);

            Console.WriteLine("Mac platform init");

            if (result != 0)
            {
                _selfHandle.Free();
                throw new Exception($"Failed to create MIDI client: {result}");
            }
        }

        [MonoPInvokeCallback(typeof(MIDINotifyProc))]
        private static void MidiNotifyStatic(ref MIDINotification notification, IntPtr refCon)
        {
            if (refCon == IntPtr.Zero) return;
            
            var handle = GCHandle.FromIntPtr(refCon);
            if (!handle.IsAllocated) return;

            var instance = handle.Target as InputService;
            instance?.MidiNotifyInstance(ref notification);
        }

        private void MidiNotifyInstance(ref MIDINotification notification)
        {
            switch (notification.MessageID)
            {
                case MIDIMessageID.ObjectAdded:
                    if (_initialized)
                    {
                        lock (_devices)
                        {
                            ConstructMidiDevices();
                            _devices = new List<IMidiInputDevice>(_deviceDictionary.SelectMany(d => d.Value));
                        }

                        OnInputDeviceReconnected?.Invoke(this, EventArgs.Empty);
                    }
                    break;
                default:
                    break;
            }
        }
    }
    #endif
}
