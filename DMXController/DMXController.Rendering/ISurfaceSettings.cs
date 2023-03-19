using System;
using System.Collections.Generic;
using System.Text;

namespace ShowControl.Rendering
{
    public class RenderSettings
    {
        public RenderSettings(
            int maxBrightness)
        {
            MasterFader = maxBrightness;
        }

        public int MasterFader { get; private set; }
    }
}
