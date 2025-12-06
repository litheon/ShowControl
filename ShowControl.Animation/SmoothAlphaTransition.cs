using System;
using System.Collections.Generic;
using System.Drawing;
using System.Text;

namespace ShowControl.Animation
{
    public class SmoothAlphaTransition
    {
        private int[] _skippedAnimations;
        private int[] _remainingDistributed;
        private int _maxDistanceIndex;
        private int _maxDistance;
        private byte[] _source;
        private byte[] _target;
        private byte[] _current;
        private object _transitionLock = new object();
        private bool _recomputeMaxDistance = true;
        public SmoothAlphaTransition()
        {

        }

        public bool Transition(byte[] source, byte[] target, byte[] current, bool recomputeDistance = false)
        {
            lock(_transitionLock)
            {
                if (_source != source || _target != target || _current != current)
                {
                    _source = source;
                    _target = target;
                    _current = current;
                    _skippedAnimations = new int[_source.Length];
                    _remainingDistributed = new int[_source.Length];
                }

                if (_recomputeMaxDistance || recomputeDistance)
                {
                    CalculateMaxDistance();
                }

                var aRemainingDistance = Math.Abs(current[_maxDistanceIndex] - target[_maxDistanceIndex]);

                var remainingDistance = aRemainingDistance;
                if (remainingDistance == 0)
                {
                    if (System.Diagnostics.Debugger.IsAttached)
                    {
                        for (int i = 0; i < current.Length; i++)
                        {
                            if (current[i] != target[i])
                            {
                                System.Diagnostics.Debugger.Break();
                            }
                        }
                    }

                    for (int i = 0; i < _remainingDistributed.Length; i++)
                    {
                        _remainingDistributed[i] = 0;
                    }

                    for (int i = 0; i < _skippedAnimations.Length; i++)
                    {
                        _skippedAnimations[i] = 0;
                    }
                }

                for (int i = 0; i < current.Length; i++)
                {
                    byte A = CalculateColorChange(current[i], source[i], target[i], remainingDistance, ref _skippedAnimations[i], ref _remainingDistributed[i]);

                    current[i] = A;
                }

                _recomputeMaxDistance = remainingDistance == 0;

                return _recomputeMaxDistance;
            }
        }

        private void CalculateMaxDistance()
        {
            _maxDistance = 0;
            for (int i = 0; i < _target.Length && i < _current.Length; i++)
            {
                if (_source[i] != _target[i])
                {
                    var originalADistance = Math.Abs(_target[i] - _source[i]);
                    DetermineMaxDistanceIndex(ref _maxDistanceIndex, ref _maxDistance, i, originalADistance);
                }

                static void DetermineMaxDistanceIndex(ref int maxDistanceIndex, ref int originalMaxDistance, int i, int distance)
                {
                    if (distance > originalMaxDistance)
                    {
                        originalMaxDistance = distance;
                        maxDistanceIndex = i;
                    }
                }
            }
        }

        private byte CalculateColorChange(int current, byte source, byte target, int remainingDistance,
            ref int skippedAnimations, ref int remainingDistributed)
        {
            if (current == target)
            {
                return target;
            }

            if (System.Diagnostics.Debugger.IsAttached)
            {
                if (source < target && current < source)
                {
                    System.Diagnostics.Debugger.Break();
                }
            }

            bool add = source < target;
            //The original distance this light had to target
            var animatesToTarget = Math.Abs(source - target);
            //The number of Animate calls that should occur before this light moves up by one to target
            var animatesPerDelta = animatesToTarget > 0 ? _maxDistance / animatesToTarget : 1;
            //The number of Animate calls that need to be made up for this light for target
            var originalRemaining = animatesToTarget > 0 && _maxDistance / animatesPerDelta < animatesToTarget ? _maxDistance / animatesPerDelta % animatesToTarget : 0;
            //On average the number of remaining to target that should be distributed per Animate for this light 
            var remainingRatePerAnimate = (double)originalRemaining / _maxDistance;
            //Should we animate to target for this light in this call?
            if (animatesPerDelta <= 1)
            {
                //yes
                current = add ? current + 1 : current - 1;
            }
            else if (animatesPerDelta > 1)
            {
                if (skippedAnimations < animatesPerDelta - 1)
                {
                    skippedAnimations++;
                }
                else
                {
                    current = add ? current + 1 : current - 1;
                    skippedAnimations = 0;
                }
            }

            if ((double)(originalRemaining - remainingDistributed) / (remainingDistance) > remainingRatePerAnimate)
            {
                current = add ? current + 1 : current - 1;
                remainingDistributed++;
            }

            if (System.Diagnostics.Debugger.IsAttached)
            {
                if (current > 255 || current < 0)
                {
                    System.Diagnostics.Debugger.Break();
                }
            }

            return (byte)current;
        }
    }
}
