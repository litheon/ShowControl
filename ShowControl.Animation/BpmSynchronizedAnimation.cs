using ShowControl.Devices.Audio;
using ShowControl.Devices.Input;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;

namespace ShowControl.Animation
{
    public abstract class BpmSynchronizedAnimation : MidiAwareAnimation
    {
        private IAudioService _audioService;
        private Thread _timerThread;
        private CancellationToken _threadCancellationToken;
        private ManualResetEvent _timerEnabledEvent;
        private float _bpm = 0.0f;
        public BpmSynchronizedAnimation(
            IAudioService audioService,
            CancellationToken threadCancellationToken)
        {
            _audioService = audioService;
            _threadCancellationToken = threadCancellationToken;
            _timerEnabledEvent = new ManualResetEvent(false);
            _timerThread = new Thread(TimerThreadStart);
            _timerThread.Start();
        }

        public void Start()
        {
            _timerEnabledEvent.Set();
            _audioService.AudioFrameProcessed += AudioService_OnAudioFrameProcessed;
        }

        public override void Stop()
        {
            _timerEnabledEvent.Reset();
            _audioService.AudioFrameProcessed -= AudioService_OnAudioFrameProcessed;
        }

        private void AudioService_OnAudioFrameProcessed(object sender, AudioFrameProcessedEventArgs e)
        {
            using(e)
            {
                _bpm = e.Bpm;
                if (_bpm <= 40f)
                {
                    _timerEnabledEvent.Reset();
                }
                else
                {
                    _timerEnabledEvent.Set();
                }
            }
        }

        private void TimerThreadStart()
        {
            while(!_threadCancellationToken.IsCancellationRequested)
            {
                _timerEnabledEvent.WaitOne();
                var threadSleepDeltaMs = ((float)_bpm / 60 / 400) * 1000;
                var sleepTime = Convert.ToInt32(threadSleepDeltaMs);
                Thread.Sleep(sleepTime);
                OnBpmEvent(this);
            }
        }

        public abstract void OnBpmEvent(object sender);

    }
}
