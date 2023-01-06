using System;
using System.Collections.Generic;
using System.Text;

namespace ShowControl.Animation
{
    public interface IKeyAnimation : IAnimation
    {
        public Pixel[] GetNextFrame();
    }
}
