using System;
using System.Threading;

namespace ShowControl.Utilities
{
    public static class InterlockedExtensions
    {
        public static int InterlockedIncrement(this ref int target, int min, int max)
        {
            int value = Interlocked.CompareExchange(ref target, -1, -1);
            value += 1;
            if (value > max)
            {
                value = min;
            }
            else if (value < min)
            {
                value = max;
            }
            Interlocked.Exchange(ref target, value);
            return value;
        }
        public static int InterlockedDecrement(this ref int target, int min, int max)
        {
            int value = Interlocked.CompareExchange(ref target, -1, -1);
            value -= 1;
            if (value > max)
            {
                value = min;
            }
            else if (value < min)
            {
                value = max;
            }
            Interlocked.Exchange(ref target, value);
            return value;
        }

        public static int InterlockedGet(this ref int target)
        {
            return Interlocked.CompareExchange(ref target, -1, -1);
        }
    }
}
