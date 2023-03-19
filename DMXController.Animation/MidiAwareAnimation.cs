using ShowControl.Devices.Input;
using System;
using System.Collections.Generic;
using System.Text;

namespace ShowControl.Animation
{
    public enum AnimationMode { Manual, StaticColor, Automatic, SoundReactive };
    public abstract class MidiAwareAnimation : IAnimation, IMidiAware
    {
        private PadState[] _padStates;

        public int MasterFader { get; set; }

        public event EventHandler<DeviceStateChangeEventArgs> MidiAware_DeviceStateChangeEvent;


        public MidiAwareAnimation()
        {
            
        }

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
                    SetPadState(e.PadNumber.Value, new PadState(PadIllumination.Solid, System.Drawing.Color.Silver));
                }
                else if (e.ButtonDirection == ButtonDirection.Release)
                {
                    if (_padStates != null && _padStates.Length >= e.PadNumber.Value)
                    {
                        SetPadState(e.PadNumber.Value, _padStates[e.PadNumber.Value]);
                    }
                    else
                    {
                        SetPadState(e.PadNumber.Value, new PadState(PadIllumination.Solid, System.Drawing.Color.Black));
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

        internal void AssignPadStates(PadState[] states)
        {
            _padStates = states;
        }

        internal void SetPadState(int index, PadState state)
        {
            MidiAware_DeviceStateChangeEvent?.Invoke(this, new DeviceStateChangeEventArgs(index, state));
        }

        internal void SetNavigationColor(System.Drawing.Color navigationColor)
        {
            MidiAware_DeviceStateChangeEvent?.Invoke(this, new DeviceStateChangeEventArgs(navigationColor));
        }
    }
}
