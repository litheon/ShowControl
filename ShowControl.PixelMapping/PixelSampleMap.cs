using System;

namespace ShowControl.PixelMapping
{
    /// <summary>One sampled pixel colour. Deliberately independent of any UI or DMX colour type.</summary>
    public readonly record struct RgbSample(byte R, byte G, byte B);

    /// <summary>
    /// Precompiled lookup from frame index to a byte offset in the shader's RGBA image.
    /// <para>
    /// Immutable, so it can be published to the render thread with a single volatile write and read
    /// once per frame without a lock. This replaces the hand-written strip arithmetic that used to
    /// live inside <c>ShaderAnimation.AnimateFrame</c>.
    /// </para>
    /// </summary>
    public sealed class PixelSampleMap
    {
        /// <summary>Offset value meaning "emit black": a blanked pixel, or one dragged off the image.</summary>
        public const int Blank = -1;

        private readonly int[] _byteOffsets;

        public PixelSampleMap(int imageWidth, int imageHeight, int[] byteOffsets)
        {
            if (imageWidth <= 0) throw new ArgumentOutOfRangeException(nameof(imageWidth));
            if (imageHeight <= 0) throw new ArgumentOutOfRangeException(nameof(imageHeight));

            ImageWidth = imageWidth;
            ImageHeight = imageHeight;
            _byteOffsets = byteOffsets ?? throw new ArgumentNullException(nameof(byteOffsets));
            RequiredImageBytes = imageWidth * imageHeight * 4;
        }

        public int ImageWidth { get; }

        public int ImageHeight { get; }

        public int PixelCount => _byteOffsets.Length;

        /// <summary>Length the RGBA buffer handed to <see cref="Sample"/> must have.</summary>
        public int RequiredImageBytes { get; }

        public ReadOnlySpan<int> ByteOffsets => _byteOffsets;

        /// <summary>
        /// Reads one colour per mapped pixel out of a tightly packed RGBA8 image
        /// (stride == width * 4, which is what the graphics host's staging readback produces).
        /// </summary>
        public void Sample(ReadOnlySpan<byte> rgbaImage, Span<RgbSample> destination)
        {
            if (rgbaImage.Length < RequiredImageBytes)
            {
                throw new ArgumentException(
                    $"Expected at least {RequiredImageBytes} bytes for a {ImageWidth}x{ImageHeight} RGBA image, got {rgbaImage.Length}.",
                    nameof(rgbaImage));
            }

            int[] offsets = _byteOffsets;
            if (destination.Length < offsets.Length)
            {
                throw new ArgumentException(
                    $"Destination holds {destination.Length} samples but the map has {offsets.Length} pixels.",
                    nameof(destination));
            }

            for (int i = 0; i < offsets.Length; i++)
            {
                int offset = offsets[i];
                destination[i] = offset < 0
                    ? default
                    : new RgbSample(rgbaImage[offset], rgbaImage[offset + 1], rgbaImage[offset + 2]);
            }
        }
    }
}
