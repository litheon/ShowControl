using System.Collections.Generic;
using ShowControl.ControlPanel.Editor;
using ShowControl.Devices.Audio;

namespace ShowControl.ControlPanel.Maui
{
    /// <summary>
    /// The platform half of the capture-device picker: maps the editor project's
    /// <see cref="IAudioDeviceSelector"/> onto <see cref="IAudioService"/>, which owns
    /// PortAudio. Pure adapter — resilience (revert on failure, never throw) lives in the
    /// service.
    /// </summary>
    public sealed class AudioDeviceSelector : IAudioDeviceSelector
    {
        private readonly IAudioService _audioService;

        public AudioDeviceSelector(IAudioService audioService)
        {
            _audioService = audioService;
        }

        public IReadOnlyList<AudioDeviceChoice> ListInputDevices()
        {
            var devices = _audioService.GetInputDevices();
            var choices = new List<AudioDeviceChoice>(devices.Count);
            foreach (var device in devices)
            {
                choices.Add(new AudioDeviceChoice(device.Index, device.Name, device.ChannelCount, device.IsActive));
            }
            return choices;
        }

        public bool SelectInputDevice(int index) => _audioService.SelectInputDevice(index);

        public (int Left, int Right) ActiveChannels => _audioService.ActiveInputChannels;

        public bool SelectInputChannels(int left, int right) => _audioService.SelectInputChannels(left, right);
    }
}
