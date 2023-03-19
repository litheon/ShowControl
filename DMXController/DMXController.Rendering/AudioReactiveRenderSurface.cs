using ShowControl.Devices.Audio;
using System;
using System.Collections.Generic;
using System.Text;

namespace ShowControl.Rendering
{
    public class AudioReactiveRenderSurface : RenderSurface
    {
        private IAudioService _audioService;
        public AudioReactiveRenderSurface(
            IAudioService audioService,
            RenderSettings renderSettings = null,
            params IRenderSurface[] renderSurfaces
            ) : base(renderSettings, renderSurfaces)
        {
            _audioService = audioService;
        }
    }
}
