using System;
using System.Collections.Generic;
using System.Text;

namespace ShowControl.Rendering
{
    public static class RenderSettingsExtensions
    {
        public static RenderSettings Combine(this RenderSettings renderSettings, RenderSettings target)
        {
            if (renderSettings == null && target == null)
            {
                return new RenderSettings();
            }
            else if (renderSettings == null)
            {
                return target;
            }
            else if (target == null)
            {
                return renderSettings;
            }
            return new RenderSettings(Math.Min(renderSettings.MasterFader, target.MasterFader), Math.Max(renderSettings.UV, target.UV));
        }
    }
}
