using ShowControl.Devices.Input;
using System;
using System.Collections.Generic;
using System.Text;

namespace ShowControl.Animation
{
    public interface ITransformAnimation : IAnimation
    {
        public Pixel[] GetNextFrame(Pixel[] keyFrame);
    }
}
