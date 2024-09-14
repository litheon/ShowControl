using System;
using System.Collections.Generic;
using System.Text;

namespace ShowControl.Rendering
{
    public class RenderSettings
    {
        public RenderSettings(
            int maxBrightness = 255,
            int uv = 0)
        {
            MasterFader = maxBrightness;
            UV = uv;
        }

        public int MasterFader { get; set; }

        public int UV { get; set; }
    }
}
