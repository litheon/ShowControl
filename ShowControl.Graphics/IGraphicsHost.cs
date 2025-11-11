using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ShowControl.Graphics
{
    public enum Shader { Kaleidoscope, Geometry};
    public interface IGraphicsHost
    {
        public RenderedImageHandle<byte> GetRenderedImage();
        public int Width { get;  }
        public int Height { get; }
        public System.Drawing.Color[] Colors { get; set; }
        public void SetShader(Shader shader);
    }
}
