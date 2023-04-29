using System;
using System.Collections.Generic;
using System.Text;

namespace ShowControl.Rendering
{
    public class RenderSettings
    {
        public RenderSettings(
            int maxBrightness = 255)
        {
            MasterFader = maxBrightness;
        }

        public int MasterFader { get; set; }
    }
}
