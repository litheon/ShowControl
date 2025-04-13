using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;

namespace ShowControl.Animation
{
    public class SmoothColorTransition
    {
        private ColorStat[] _skippedAnimations;
        private ColorStat[] _remainingDistributed;
        private int _maxDistanceIndex = -1;
        private int _maxDistance = -1;
        private Color[] _source;
        private Color[] _target;
        private Color[] _current;
        private object _transitionLock = new object();
        private bool _recomputeMaxDistance = true;
        public SmoothColorTransition()
        {

        }

        public bool Transition(Color[] source, Color[] target, Color[] current, bool recomputeDistance = false)
        {
            lock(_transitionLock)
            {
                if (_source != source || _target != target || _current != current)
                {
                    _source = source;
                    _target = target;
                    _current = current;
                    _skippedAnimations = new ColorStat[current.Length];
                    _remainingDistributed = new ColorStat[current.Length];
                }

                if (_recomputeMaxDistance)
                {
                    for (int i = 0; i < current.Length; i++)
                    {
                        if (current[i] != source[i])
                        {
                            break;
                        }
                    }
                }

                if (System.Diagnostics.Debugger.IsAttached && _maxDistance != -1 && !recomputeDistance && !_recomputeMaxDistance)
                {
                    for (int i = 0; i < current.Length; i++)
                    {
                        if (Math.Abs(current[i].A - target[i].A) > _maxDistance ||
                            Math.Abs(current[i].R - target[i].R) > _maxDistance ||
                            Math.Abs(current[i].G - target[i].G) > _maxDistance ||
                            Math.Abs(current[i].B - target[i].B) > _maxDistance)
                        {
                            System.Diagnostics.Debugger.Break();
                        }
                        if ((source[i].A < target[i].A && current[i].A < source[i].A) ||
                            (source[i].A > target[i].A && current[i].A > source[i].A))
                        {
                            System.Diagnostics.Debugger.Break();
                        }
                        if ((source[i].R < target[i].R && current[i].R < source[i].R) ||
                            (source[i].R > target[i].R && current[i].R > source[i].R))
                        {
                            System.Diagnostics.Debugger.Break();
                        }
                        if ((source[i].G < target[i].G && current[i].G < source[i].G) ||
                            (source[i].G > target[i].G && current[i].G > source[i].G))
                        {
                            System.Diagnostics.Debugger.Break();
                        }
                        if ((source[i].B < target[i].B && current[i].B < source[i].B) ||
                            (source[i].B > target[i].B && current[i].B > source[i].B))
                        {
                            System.Diagnostics.Debugger.Break();
                        }
                    }
                }

                if (_recomputeMaxDistance || recomputeDistance)
                {
                    CalculateMaxDistance();
                }

                var aRemainingDistance = Math.Abs(current[_maxDistanceIndex].A - target[_maxDistanceIndex].A);
                var rRemainingDistance = Math.Abs(current[_maxDistanceIndex].R - target[_maxDistanceIndex].R);
                var gRemainingDistance = Math.Abs(current[_maxDistanceIndex].G - target[_maxDistanceIndex].G);
                var bRemainingDistance = Math.Abs(current[_maxDistanceIndex].B - target[_maxDistanceIndex].B);

                var remainingDistance = Math.Max(Math.Max(rRemainingDistance, aRemainingDistance), Math.Max(gRemainingDistance, bRemainingDistance));
                if (remainingDistance == 0)
                {
                    if (System.Diagnostics.Debugger.IsAttached)
                    {
                        for (int i = 0; i < current.Length; i++)
                        {
                            if (current[i].A != target[i].A ||
                                current[i].R != target[i].R ||
                                current[i].G != target[i].G ||
                                current[i].B != target[i].B)
                            {
                                System.Diagnostics.Debugger.Break();
                            }
                        }
                    }

                    for (int i = 0; i < _remainingDistributed.Length; i++)
                    {
                        _remainingDistributed[i].a = 0;
                        _remainingDistributed[i].r = 0;
                        _remainingDistributed[i].g = 0;
                        _remainingDistributed[i].b = 0;
                    }

                    for (int i = 0; i < _skippedAnimations.Length; i++)
                    {
                        _skippedAnimations[i].a = 0;
                        _skippedAnimations[i].r = 0;
                        _skippedAnimations[i].g = 0;
                        _skippedAnimations[i].b = 0;
                    }
                }

                for (int i = 0; i < current.Length; i++)
                {
                    byte A = CalculateColorChange(current[i].A, source[i].A, target[i].A, remainingDistance, ref _skippedAnimations[i].a, ref _remainingDistributed[i].a);
                    byte R = CalculateColorChange(current[i].R, source[i].R, target[i].R, remainingDistance, ref _skippedAnimations[i].r, ref _remainingDistributed[i].r);
                    byte G = CalculateColorChange(current[i].G, source[i].G, target[i].G, remainingDistance, ref _skippedAnimations[i].g, ref _remainingDistributed[i].g);
                    byte B = CalculateColorChange(current[i].B, source[i].B, target[i].B, remainingDistance, ref _skippedAnimations[i].b, ref _remainingDistributed[i].b);

                    current[i] = Color.FromArgb(A, R, G, B);
                }

                if (System.Diagnostics.Debugger.IsAttached)
                {
                    for (int i = 0; i < current.Length; i++)
                    {
                        if (Math.Abs(current[i].A - target[i].A) > _maxDistance ||
                            Math.Abs(current[i].R - target[i].R) > _maxDistance ||
                            Math.Abs(current[i].G - target[i].G) > _maxDistance ||
                            Math.Abs(current[i].B - target[i].B) > _maxDistance)
                        {
                            System.Diagnostics.Debugger.Break();
                        }
                        if ((source[i].A < target[i].A && current[i].A < source[i].A) ||
                            (source[i].A > target[i].A && current[i].A > source[i].A))
                        {
                            System.Diagnostics.Debugger.Break();
                        }
                        if ((source[i].R < target[i].R && current[i].R < source[i].R) ||
                            (source[i].R > target[i].R && current[i].R > source[i].R))
                        {
                            System.Diagnostics.Debugger.Break();
                        }
                        if ((source[i].G < target[i].G && current[i].G < source[i].G) ||
                            (source[i].G > target[i].G && current[i].G > source[i].G))
                        {
                            System.Diagnostics.Debugger.Break();
                        }
                        if ((source[i].B < target[i].B && current[i].B < source[i].B) ||
                            (source[i].B > target[i].B && current[i].B > source[i].B))
                        {
                            System.Diagnostics.Debugger.Break();
                        }
                    }
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
                if (_source[i].A != _target[i].A)
                {
                    var originalADistance = Math.Abs(_target[i].A - _source[i].A);
                    DetermineMaxDistanceIndex(ref _maxDistanceIndex, ref _maxDistance, i, originalADistance);
                }
                if (_source[i].R != _target[i].R)
                {
                    var originalRDistance = Math.Abs(_target[i].R - _source[i].R);
                    DetermineMaxDistanceIndex(ref _maxDistanceIndex, ref _maxDistance, i, originalRDistance);
                }
                if (_source[i].G != _target[i].G)
                {
                    var originalGDistance = Math.Abs(_target[i].G - _source[i].G);
                    DetermineMaxDistanceIndex(ref _maxDistanceIndex, ref _maxDistance, i, originalGDistance);
                }
                if (_source[i].B != _target[i].B)
                {
                    var originalBDistance = Math.Abs(_target[i].B - _source[i].B);
                    DetermineMaxDistanceIndex(ref _maxDistanceIndex, ref _maxDistance, i, originalBDistance);
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
