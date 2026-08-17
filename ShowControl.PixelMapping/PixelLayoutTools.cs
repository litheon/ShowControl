using System;
using System.Drawing;

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
        /// Reverses the run so the fixture is driven from the other end: pixel 0 takes the last
        /// pixel's position, pixel 1 the second to last, and so on.
        /// <para>
        /// The positions themselves are untouched as a set — the strip does not move, only the order
        /// the pixels are addressed in — which is the point: it is the fix for a strip whose data-in
        /// end is the opposite one to what the map assumes. Blanked runs are deliberately left
        /// alone; they are indices into the wire, and the wire is what has been re-ended.
        /// </para>
        /// <para>
        /// Offsets are then rebased so the first is (0,0), keeping the convention that a fixture's
        /// origin is where its first pixel sits. The returned shift is what the caller must add to
        /// the origin to leave the run exactly where it was; applying the operation twice restores
        /// the original offsets and origin exactly.
        /// </para>
        /// </summary>
        public static PointF ReverseDirection(float[] offsets)
        {
            if (offsets == null) throw new ArgumentNullException(nameof(offsets));
            if (offsets.Length % 2 != 0)
            {
                throw new ArgumentException("Offsets must hold an x and a y per pixel.", nameof(offsets));
            }

            int pixelCount = offsets.Length / 2;
            if (pixelCount == 0)
            {
                return default;
            }

            for (int i = 0, j = pixelCount - 1; i < j; i++, j--)
            {
                (offsets[i * 2], offsets[j * 2]) = (offsets[j * 2], offsets[i * 2]);
                (offsets[(i * 2) + 1], offsets[(j * 2) + 1]) = (offsets[(j * 2) + 1], offsets[(i * 2) + 1]);
            }

            float shiftX = offsets[0];
            float shiftY = offsets[1];
            for (int i = 0; i < offsets.Length; i += 2)
            {
                offsets[i] -= shiftX;
                offsets[i + 1] -= shiftY;
            }

            return new PointF(shiftX, shiftY);
        }

        /// <summary>
        /// The average distance between neighbouring pixels — the run's spacing, read back off the
        /// stored positions because the map does not record it. Exact for an evenly spaced run,
        /// which is what the editor offers it for; a bent or uneven fixture gets the mean, which is
        /// the only honest single number for one.
        /// </summary>
        /// <returns>Zero for a fixture of fewer than two pixels, which has no spacing.</returns>
        public static float MeasureSpacing(float[] offsets)
        {
            if (offsets == null) throw new ArgumentNullException(nameof(offsets));
            if (offsets.Length % 2 != 0)
            {
                throw new ArgumentException("Offsets must hold an x and a y per pixel.", nameof(offsets));
            }

            int pixelCount = offsets.Length / 2;
            if (pixelCount < 2)
            {
                return 0f;
            }

            double total = 0d;
            for (int i = 1; i < pixelCount; i++)
            {
                float dx = offsets[i * 2] - offsets[(i - 1) * 2];
                float dy = offsets[(i * 2) + 1] - offsets[((i - 1) * 2) + 1];
                total += Math.Sqrt((dx * dx) + (dy * dy));
            }

            return (float)(total / (pixelCount - 1));
        }

        /// <summary>
        /// Respaces a fixture to <paramref name="spacing"/> image units between neighbouring pixels.
        /// <para>
        /// A uniform scale about pixel 0 rather than a regenerated line: it leaves the run's
        /// direction and any bend in it alone, and is exact for the evenly spaced case. Pixel 0 is
        /// the anchor because it is where the fixture's origin sits, so respacing does not also move
        /// the fixture. Pure and in-place on a copy the caller owns.
        /// </para>
        /// </summary>
        public static void SetSpacing(float[] offsets, float spacing)
        {
            if (offsets == null) throw new ArgumentNullException(nameof(offsets));
            if (spacing <= 0f || !float.IsFinite(spacing))
            {
                throw new ArgumentOutOfRangeException(nameof(spacing), spacing, "Spacing must be a positive number.");
            }

            float current = MeasureSpacing(offsets);
            if (current <= 0f)
            {
                // Nothing to scale: one pixel, or every pixel already on top of pixel 0. Either way
                // there is no direction to space them out along, and inventing one would be a lie.
                return;
            }

            Scale(offsets, spacing / current);
        }

        /// <summary>
        /// Scales every offset about pixel 0. The general form of respacing, kept separate because
        /// the editor also wants it directly for "make this fixture bigger".
        /// </summary>
        public static void Scale(float[] offsets, float factor)
        {
            if (offsets == null) throw new ArgumentNullException(nameof(offsets));
            if (offsets.Length % 2 != 0)
            {
                throw new ArgumentException("Offsets must hold an x and a y per pixel.", nameof(offsets));
            }
            if (!float.IsFinite(factor))
            {
                throw new ArgumentOutOfRangeException(nameof(factor), factor, "Scale factor must be a finite number.");
            }

            float anchorX = offsets.Length >= 2 ? offsets[0] : 0f;
            float anchorY = offsets.Length >= 2 ? offsets[1] : 0f;

            for (int i = 0; i < offsets.Length; i += 2)
            {
                offsets[i] = anchorX + ((offsets[i] - anchorX) * factor);
                offsets[i + 1] = anchorY + ((offsets[i + 1] - anchorY) * factor);
            }
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
