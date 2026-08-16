using System;

namespace ShowControl.PixelMapping
{
    /// <summary>
    /// Byte order the physical controller expects for a pixel's colour channels.
    /// The name lists the channels in wire order, so <see cref="Grb"/> sends green first.
    /// </summary>
    public enum ColorOrder
    {
        Rgb = 0,
        Grb,
        Bgr,
        Rbg,
        Gbr,
        Brg
    }

    public static class ColorOrderExtensions
    {
        /// <summary>
        /// Permutes the first three channels in place. Channels beyond the third (white, amber,
        /// and so on) are left alone, and <see cref="ColorOrder.Rgb"/> is the identity, which is
        /// what keeps the default map byte-identical to the pre-pixel-map output.
        /// </summary>
        public static void Apply(this ColorOrder order, Span<byte> channels)
        {
            if (order == ColorOrder.Rgb || channels.Length < 3)
            {
                return;
            }

            byte r = channels[0];
            byte g = channels[1];
            byte b = channels[2];

            switch (order)
            {
                case ColorOrder.Grb: channels[0] = g; channels[1] = r; channels[2] = b; break;
                case ColorOrder.Bgr: channels[0] = b; channels[1] = g; channels[2] = r; break;
                case ColorOrder.Rbg: channels[0] = r; channels[1] = b; channels[2] = g; break;
                case ColorOrder.Gbr: channels[0] = g; channels[1] = b; channels[2] = r; break;
                case ColorOrder.Brg: channels[0] = b; channels[1] = r; channels[2] = g; break;
            }
        }
    }
}
