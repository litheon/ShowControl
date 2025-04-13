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
            byte MasterFader = 0;
            if (renderSettings.MasterFader <= target.MasterFader)
            {
                MasterFader = (byte)renderSettings.MasterFader;
            }
            else
            {
                MasterFader = Convert.ToByte((float)target.MasterFader / (float)renderSettings.MasterFader * byte.MaxValue);
            }

            return new RenderSettings(MasterFader, Math.Max(renderSettings.UV, target.UV));
        }
    }
}
