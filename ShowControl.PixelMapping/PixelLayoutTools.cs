using System;

namespace ShowControl.PixelMapping
{
    /// <summary>
    /// Editor-side operations that produce or rewrite a fixture's <see cref="FixtureDefinition.PixelOffsets"/>.
    /// Nothing here is persisted — spacing, angle and pivot points exist only as inputs to these
    /// functions, and the map records the resulting positions.
    /// </summary>
    public static class PixelLayoutTools
    {
        /// <summary>
        /// Offsets for a straight run of <paramref name="pixelCount"/> pixels. This is what the
        /// "add a fixture with N pixels" flow calls; the caller's spacing and angle are consumed
        /// here and then forgotten.
        /// </summary>
        public static float[] GenerateLine(int pixelCount, float spacing = 1f, float angleDegrees = 0f)
        {
            if (pixelCount <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(pixelCount), pixelCount, "A fixture needs at least one pixel.");
            }

            GetDirection(angleDegrees, out float dx, out float dy);

            var offsets = new float[pixelCount * 2];
            for (int i = 0; i < pixelCount; i++)
            {
                // Multiplication rather than accumulation: 720 accumulated adds drift, and this is
                // bit-exact at spacing 1 / angle 0, which the legacy-parity test depends on.
                offsets[i * 2] = i * spacing * dx;
                offsets[(i * 2) + 1] = i * spacing * dy;
            }
            return offsets;
        }

        /// <summary>
        /// Rotates every pixel from <paramref name="fromIndex"/> onward about that pixel, leaving
        /// the run before it untouched — bending a strip at a point, which is what the editor's
        /// pivot gesture does. Pure and in-place on a copy the caller owns.
        /// </summary>
        public static void RotateSection(float[] offsets, int fromIndex, float turnDegrees)
        {
            if (offsets == null) throw new ArgumentNullException(nameof(offsets));
            if (offsets.Length % 2 != 0)
            {
                throw new ArgumentException("Offsets must hold an x and a y per pixel.", nameof(offsets));
            }

            int pixelCount = offsets.Length / 2;
            if (fromIndex < 0 || fromIndex >= pixelCount)
            {
                throw new ArgumentOutOfRangeException(nameof(fromIndex));
            }

            GetDirection(turnDegrees, out float cos, out float sin);

            float pivotX = offsets[fromIndex * 2];
            float pivotY = offsets[(fromIndex * 2) + 1];

            for (int i = fromIndex + 1; i < pixelCount; i++)
            {
                float x = offsets[i * 2] - pivotX;
                float y = offsets[(i * 2) + 1] - pivotY;
                offsets[i * 2] = pivotX + ((x * cos) - (y * sin));
                offsets[(i * 2) + 1] = pivotY + ((x * sin) + (y * cos));
            }
        }

        /// <summary>
        /// Unit vector for an angle, snapped on the quarter turns. <c>MathF.Cos(MathF.PI / 2)</c>
        /// is -4.37e-08, not zero, which would leave a vertical strip with 720 slightly different
        /// x offsets instead of one.
        /// </summary>
        private static void GetDirection(float angleDegrees, out float dx, out float dy)
        {
            float normalized = angleDegrees % 360f;
            if (normalized < 0f)
            {
                normalized += 360f;
            }

            switch (normalized)
            {
                case 0f: dx = 1f; dy = 0f; return;
                case 90f: dx = 0f; dy = 1f; return;
                case 180f: dx = -1f; dy = 0f; return;
                case 270f: dx = 0f; dy = -1f; return;
            }

            float radians = normalized * (MathF.PI / 180f);
            dx = MathF.Cos(radians);
            dy = MathF.Sin(radians);
        }
    }
}
