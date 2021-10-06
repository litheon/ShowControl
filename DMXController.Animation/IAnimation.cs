using ShowControl.Devices.Input;
using System;

namespace ShowControl.Animation
{
    public interface IAnimation
    {
        public bool IsActive { get; set; }
        public Pixel[] GetNextFrame();
    }
}
