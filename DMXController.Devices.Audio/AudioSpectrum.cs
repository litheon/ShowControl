using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ShowControl.Devices.Audio
{
    public class AudioSpectrum : IDisposable
    {
        public Point[] Points { get; init; }
        private ArrayPool<Point> _pointsPool;
        private bool _disposed;
        public AudioSpectrum(ArrayPool<Point> sourcePool, Point[] points)
        {
            _pointsPool = sourcePool;
            Points = points;
        }
        protected virtual void Dispose(bool disposing)
        {
            if (_disposed)
            {
                return;
            }

            if (disposing)
            {
                _pointsPool.Return(Points);
            }

            _disposed = true;
        }

        public void Dispose()
        {
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
    }
}
