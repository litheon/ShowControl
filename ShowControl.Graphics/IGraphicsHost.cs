using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ShowControl.Graphics
{
    public interface IGraphicsHost
    {
        public RenderedImageHandle<byte> GetRenderedImage();
        public int Width { get;  }
        public int Height { get; }
    }
}
