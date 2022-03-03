using ShowControl.Devices.Input;
using System;
using System.Collections.Generic;
using System.Text;

namespace ShowControl.Animation
{
    public enum AnimationMode { Manual, StaticColor, Automatic, SoundReactive };
    public abstract class MidiAwareAnimation : IAnimation, IMidiAware
    {
        private System.Drawing.Color[] _padColors;
        public event EventHandler<DeviceStateChangeEventArgs> MidiAware_DeviceStateChangeEvent;


        public MidiAwareAnimation()
        {
            
        }

        public abstract Pixel[] GetNextFrame();
        public void MidiAware_OnMessageReceived(object sender, DeviceInputEventArgs e)
        {
            if (e.EventType == DeviceInputEventType.RotaryEncoder)
            {
                RotaryTurned(e.RotaryDirection, e.RotaryNumber.Value, e.RotaryValue);
            }
            else if (e.EventType == DeviceInputEventType.Pad)
            {
                if (e.ButtonDirection == ButtonDirection.Press)
                {
                    SetPadColor(e.PadNumber.Value, System.Drawing.Color.Silver);
                }
                else if (e.ButtonDirection == ButtonDirection.Release)
                {
                    if (_padColors != null && _padColors.Length >= e.PadNumber.Value)
                    {
                        SetPadColor(e.PadNumber.Value, _padColors[e.PadNumber.Value]);
                    }
                    else
                    {
                        SetPadColor(e.PadNumber.Value, System.Drawing.Color.Black);
                    }
                }
                PadPressed(e.ButtonDirection, e.PadNumber.Value, e.Source.PadCount);
            }
            OnMessageReceived(sender, e);
        }

        public abstract void OnMessageReceived(object sender, DeviceInputEventArgs e);

        public abstract void RotaryTurned(RotaryDirection direction, byte rotaryNumber, byte? value);

        public abstract void PadPressed(ButtonDirection direction, byte padNumber, int padCount);

        public abstract DeviceState Start(IReadOnlyMidiInputDevice inputDevice);
        public abstract void Stop();

        internal void AssignPadColors(System.Drawing.Color[] colors)
        {
            _padColors = colors;
        }

        internal void SetPadColor(int index, System.Drawing.Color color)
        {
            MidiAware_DeviceStateChangeEvent?.Invoke(this, new DeviceStateChangeEventArgs(index, color));
        }

        internal void SetNavigationColor(System.Drawing.Color navigationColor)
        {
            MidiAware_DeviceStateChangeEvent?.Invoke(this, new DeviceStateChangeEventArgs(navigationColor));
        }
    }
}
