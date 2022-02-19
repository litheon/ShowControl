using ShowControl.Devices.Input.Extensions;
using System;
using System.Collections.Generic;
using System.Text;

namespace ShowControl.Devices.Input
{
    public enum DeviceInputEventType { Pad, Navigation, Transport, RotaryEncoder};
    public enum RotaryDirection {None, Up, Down};
    public enum ButtonDirection { None, Press, Release};
    public enum NavigationButton { None, Up, Down, Left, Right};
    public enum TransportButton { None, Play, Stop };
    public class DeviceInputEventArgs
    {
        public DeviceInputEventType EventType { get; private set; }
        public IReadOnlyMidiInputDevice Source { get; private set; }
        public RotaryDirection RotaryDirection { get; private set; }
        public ButtonDirection ButtonDirection { get; private set; }
        public NavigationButton NavigationDirection { get; private set; }
        public TransportButton TransportButton { get; private set; }
        public byte? RotaryValue { get; private set; }
        public byte? RotaryNumber { get; private set; }
        public byte? PadNumber { get; private set; }
        public DeviceInputEventArgs(IMidiInputDevice source, RotaryDirection rotaryDirection, byte? rotaryNumber, byte? rotaryValue)
        {
            Source = source.AsReadOnly();
            RotaryDirection = rotaryDirection;
            RotaryValue = rotaryValue;
            RotaryNumber = rotaryNumber;
            EventType = DeviceInputEventType.RotaryEncoder;

        }
        public DeviceInputEventArgs(IMidiInputDevice source, ButtonDirection buttonDirection, byte? padNumber)
        {
            Source = source.AsReadOnly();
            ButtonDirection = buttonDirection;
            PadNumber = padNumber;
            EventType = DeviceInputEventType.Pad;
        }
        public DeviceInputEventArgs(IMidiInputDevice source, NavigationButton navigationDirection, ButtonDirection buttonDirection)
        {
            Source = source.AsReadOnly();
            ButtonDirection = buttonDirection;
            EventType = DeviceInputEventType.Navigation;
            NavigationDirection = navigationDirection;
        }
        public DeviceInputEventArgs(IMidiInputDevice source, TransportButton transportButton, ButtonDirection buttonDirection)
        {
            Source = source.AsReadOnly();
            ButtonDirection = buttonDirection;
            EventType = DeviceInputEventType.Transport;
            TransportButton = transportButton;
        }
    }
}
