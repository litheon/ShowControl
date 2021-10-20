using System;
using System.Collections.Generic;
using System.Text;

namespace ShowControl.Animation
{
    class VibrantAnimation : IAnimation
    {
        bool IAnimation.IsActive { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }

        Pixel[] IAnimation.GetNextFrame()
        {
            throw new NotImplementedException();
        }
    }
}
