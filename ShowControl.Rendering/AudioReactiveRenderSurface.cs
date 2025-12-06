using ShowControl.Devices.Audio;
using ShowControl.Devices.DMX;
using System;
using System.Collections.Generic;
using System.Linq;
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

        public static AudioReactiveRenderSurface Create(
            IAudioService audioService,
            params IRenderSurface[] renderSurfaces
            )
        {
            return new AudioReactiveRenderSurface(audioService, renderSettings: null, renderSurfaces);
        }

        public static AudioReactiveRenderSurface Create(
            IAudioService audioService,
            RenderSettings renderSettings = null,
            params IDmxDevice[] dmxDevices)
        {
            var dmxRenderSurfaces = dmxDevices.Select(d => new DmxDeviceRenderSurface(d));
            return new AudioReactiveRenderSurface(audioService, renderSettings: renderSettings, dmxRenderSurfaces.ToArray());
        }
    }
}
