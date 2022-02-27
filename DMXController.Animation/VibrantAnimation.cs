using ShowControl.Devices.Audio;
using ShowControl.Devices.Input;
using System;
using System.Collections.Generic;
using System.Text;

namespace ShowControl.Animation
{
    public class VibrantAnimation : MidiAwareAnimation
    {
        private readonly IAudioService _audioService;
        private double _lastFft = double.NegativeInfinity;
        private const int defaultFrameCooldown = AudioService.SampleRate / AudioService.FftLength / 2;
        private int frameCoolDown = 1;
        private int _pixelCount = 0;
        private Pixel[] _onFrame;
        private Pixel[] _offFrame;
        private Pixel[] _currentFrame;
        public VibrantAnimation(int pixelCount, IAudioService audioService)
        {
            _pixelCount = pixelCount;
            _audioService = audioService;
            _onFrame = new Pixel[pixelCount];
            for (int i = 0; i < pixelCount; i++)
            {
                _onFrame[i] = new Pixel(System.Drawing.Color.Red, 0, 0);
            }
            _offFrame = new Pixel[pixelCount];
            _currentFrame = _offFrame;
        }

        private void OnAudioFrameProcessed(object sender, AudioFrameProcessedEventArgs e)
        {
            var spectrum = e.AudioSpectrum;

            var one = e.AudioSpectrum[1].Y;
            var two = e.AudioSpectrum[2].Y;
            var three = e.AudioSpectrum[3].Y;

            var current = three;

            if (current > _lastFft * 1.2 && current > -50 && frameCoolDown <= 0)
            {
                _currentFrame = _onFrame;
                frameCoolDown = defaultFrameCooldown;
            }
            else if (frameCoolDown >= 1)
            {
                frameCoolDown--;
            }
            else if (frameCoolDown == 0)
            {
                frameCoolDown = -1;
                _currentFrame = _offFrame;
            }
            _lastFft = current;
        }

        public override Pixel[] GetNextFrame()
        {
            return _currentFrame;
        }

        public override void MidiAware_OnMessageReceived(object sender, DeviceInputEventArgs e)
        {
            //throw new NotImplementedException();
        }

        public override DeviceState Start(IReadOnlyMidiInputDevice inputDevice)
        {
            _audioService.AudioFrameProcessed += OnAudioFrameProcessed;
            //throw new NotImplementedException();
            return new DeviceState(new System.Drawing.Color[0], new ButtonLightState[0]);
        }

        public override void Stop()
        {
            _audioService.AudioFrameProcessed -= OnAudioFrameProcessed;
        }
    }
}
