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
        /// The run with <paramref name="pixelCount"/> pixels: the existing positions kept, the run
        /// cut short, or carried on past its last pixel in the direction and at the spacing of
        /// its last link — which is what a strip physically does when it is extended. A run of one
        /// pixel has no last link, so it grows at <paramref name="fallbackSpacing"/> and
        /// <paramref name="fallbackAngleDegrees"/>, which is how the add form lays a new one out.
        /// Returns a new array; the input is untouched.
        /// </summary>
        public static float[] SetPixelCount(float[] offsets, int pixelCount, float fallbackSpacing = 1f, float fallbackAngleDegrees = 0f)
        {
            if (offsets == null) throw new ArgumentNullException(nameof(offsets));
            if (offsets.Length % 2 != 0)
            {
                throw new ArgumentException("Offsets must hold an x and a y per pixel.", nameof(offsets));
            }
            if (pixelCount <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(pixelCount), pixelCount, "A fixture needs at least one pixel.");
            }

            int current = offsets.Length / 2;
            var result = new float[pixelCount * 2];
            Array.Copy(offsets, result, Math.Min(offsets.Length, result.Length));
            if (pixelCount <= current)
            {
                return result;
            }

            float dx, dy;
            if (current >= 2)
            {
                dx = offsets[(current - 1) * 2] - offsets[(current - 2) * 2];
                dy = offsets[((current - 1) * 2) + 1] - offsets[((current - 2) * 2) + 1];
            }
            else
            {
                GetDirection(fallbackAngleDegrees, out dx, out dy);
                dx *= fallbackSpacing;
                dy *= fallbackSpacing;
            }
            if (dx == 0f && dy == 0f)
            {
                // The last two pixels sit on top of each other: continue the way the add form would.
                GetDirection(fallbackAngleDegrees, out dx, out dy);
                dx *= fallbackSpacing;
                dy *= fallbackSpacing;
            }

            // An empty run grows from pixel 0 at the origin, as the add form lays one out.
            int anchor = Math.Max(current, 1) - 1;
            float lastX = current > 0 ? offsets[anchor * 2] : 0f;
            float lastY = current > 0 ? offsets[(anchor * 2) + 1] : 0f;
            for (int i = anchor + 1; i < pixelCount; i++)
            {
                int steps = i - anchor;
                result[i * 2] = lastX + (steps * dx);
                result[(i * 2) + 1] = lastY + (steps * dy);
            }
            return result;
        }

        /// <summary>
        /// Swings the run from <paramref name="handleIndex"/> onward about the pixel before it, so
        /// that the handle lies on the ray from that hinge toward <paramref name="targetX"/>,
        /// <paramref name="targetY"/> (in offset space: image position minus the fixture's origin).
        /// The swung section keeps its own shape — a rigid rotation, not a re-layout — so a strip
        /// that already has a corner further along keeps it.
        /// <para>
        /// This is the editor's pixel-drag gesture, and it replaced an inextensible-rope model. The
        /// rope was faithful to the physics and useless for the job: every pull propagated down the
        /// strip, a corner could not be placed because the slack rule moved only the grabbed pixel,
        /// and nothing short of re-adding the fixture got a bent strip straight again. A physical
        /// strip bends at a point and is straight between bends, and that is exactly what a hinge
        /// gives you: grab the pixel after where you want the corner, point the rest of the run.
        /// </para>
        /// <para>
        /// Grabbing pixel 0 has no pixel before it, so the hinge is pixel 0 itself and the whole
        /// run rotates about the fixture's origin — which makes the ring that marks the first pixel
        /// double as the rotate handle. Pixel 0 therefore never moves, so no rebase is needed.
        /// </para>
        /// <para>
        /// A heading within <paramref name="snapToleranceDegrees"/> of a multiple of 45° snaps to
        /// it, which is how a right angle is hit by eye. A target closer to the hinge than half the
        /// hinge-to-handle distance is ignored: the angle is meaningless there, and every bend
        /// starts with the cursor sitting on the handle, a fraction of a pixel from the hinge.
        /// </para>
        /// <para>
        /// Pure and in-place on a copy the caller owns, which must hold the <em>rest</em> positions —
        /// the offsets as they stood when the gesture began — so the same target always produces
        /// the same shape however many moves were coalesced or dropped.
        /// </para>
        /// </summary>
        /// <returns>The heading the section now points in, in degrees, or null if nothing changed.</returns>
        public static float? PointSectionAt(float[] offsets, int handleIndex, float targetX, float targetY, float snapToleranceDegrees = 0f)
        {
            if (offsets == null) throw new ArgumentNullException(nameof(offsets));
            if (offsets.Length % 2 != 0)
            {
                throw new ArgumentException("Offsets must hold an x and a y per pixel.", nameof(offsets));
            }

            int pixelCount = offsets.Length / 2;
            if (handleIndex < 0 || handleIndex >= pixelCount)
            {
                throw new ArgumentOutOfRangeException(nameof(handleIndex));
            }
            if (!float.IsFinite(targetX) || !float.IsFinite(targetY))
            {
                throw new ArgumentException("The target must be finite.");
            }

            int hinge = handleIndex == 0 ? 0 : handleIndex - 1;
            int first = hinge + 1;
            if (first >= pixelCount)
            {
                // A single pixel has nothing to swing.
                return null;
            }

            float hingeX = offsets[hinge * 2];
            float hingeY = offsets[(hinge * 2) + 1];
            float linkX = offsets[first * 2] - hingeX;
            float linkY = offsets[(first * 2) + 1] - hingeY;
            float towardX = targetX - hingeX;
            float towardY = targetY - hingeY;

            float linkLength = MathF.Sqrt((linkX * linkX) + (linkY * linkY));
            float reach = MathF.Sqrt((towardX * towardX) + (towardY * towardY));
            if (reach <= 0f || reach < linkLength * 0.5f)
            {
                return null;
            }

            float currentHeading = linkLength > 0f ? HeadingDegrees(linkX, linkY) : 0f;
            float targetHeading = HeadingDegrees(towardX, towardY);
            if (snapToleranceDegrees > 0f)
            {
                float nearest = MathF.Round(targetHeading / 45f) * 45f;
                if (MathF.Abs(NormalizeTurn(targetHeading - nearest)) <= snapToleranceDegrees)
                {
                    targetHeading = nearest % 360f;
                }
            }

            float turn = NormalizeTurn(targetHeading - currentHeading);
            if (turn == 0f)
            {
                return targetHeading;
            }

            RotateSection(offsets, hinge, turn);
            return targetHeading;
        }

        /// <summary>
        /// The total turn along the run: the signed angle from the first link's heading to the
        /// last link's, accumulated pixel by pixel so a run that winds past 180° is measured as the
        /// 360° it actually turns. Zero for a straight run, which is what the editor's "bend" field
        /// shows; the inverse of <see cref="SetBend"/> for an evenly bent one.
        /// </summary>
        /// <returns>Degrees, positive clockwise in image space (y down). Zero for fewer than three pixels.</returns>
        public static float MeasureBend(float[] offsets)
        {
            if (offsets == null) throw new ArgumentNullException(nameof(offsets));
            if (offsets.Length % 2 != 0)
            {
                throw new ArgumentException("Offsets must hold an x and a y per pixel.", nameof(offsets));
            }

            int pixelCount = offsets.Length / 2;
            if (pixelCount < 3)
            {
                return 0f;
            }

            double total = 0d;
            float? previousHeading = null;
            for (int i = 1; i < pixelCount; i++)
            {
                float dx = offsets[i * 2] - offsets[(i - 1) * 2];
                float dy = offsets[(i * 2) + 1] - offsets[((i - 1) * 2) + 1];
                if (dx == 0f && dy == 0f)
                {
                    // Two pixels on top of each other have no heading between them; skip the link
                    // rather than invent one.
                    continue;
                }

                float heading = HeadingDegrees(dx, dy);
                if (previousHeading.HasValue)
                {
                    total += NormalizeTurn(heading - previousHeading.Value);
                }
                previousHeading = heading;
            }

            return (float)total;
        }

        /// <summary>
        /// Lays the run out as an even arc turning <paramref name="totalDegrees"/> in all between
        /// its first link and its last: the same turn at every interior pixel, at the run's mean
        /// spacing, starting in the direction the first link already points. Zero straightens it
        /// along that first link; 360 is a ring (very nearly closed — the turn is spread over the
        /// interior pixels, so the final link stops one step short of the first).
        /// <para>
        /// Pixel 0 stays put, so the fixture's origin still marks it. Regenerates at the mean spacing
        /// rather than preserving each link, because the editor's tools only ever produce evenly
        /// spaced runs and a physical strip is one; any unevenness is damage from an older gesture,
        /// and this is the one that repairs it. Pure and in-place on a copy the caller owns.
        /// </para>
        /// </summary>
        public static void SetBend(float[] offsets, float totalDegrees)
        {
            if (offsets == null) throw new ArgumentNullException(nameof(offsets));
            if (!float.IsFinite(totalDegrees))
            {
                throw new ArgumentOutOfRangeException(nameof(totalDegrees), totalDegrees, "Bend must be a finite number of degrees.");
            }

            int pixelCount = offsets.Length / 2;
            float spacing = MeasureSpacing(offsets);
            if (pixelCount < 2 || spacing <= 0f)
            {
                return;
            }

            float startHeading = FirstLinkHeading(offsets);
            float perPixel = pixelCount >= 3 ? totalDegrees / (pixelCount - 2) : 0f;
            Relayout(offsets, startHeading, perPixel, spacing);
        }

        /// <summary>
        /// Makes the run a straight, evenly spaced line again, pointing from its first pixel toward
        /// where its last pixel was — the fix for a strip that was bent by accident, and the way back
        /// from any shape. Pixel 0 stays put; spacing is the run's mean spacing, so a strip that was
        /// bent and unbent measures the same as it did before. Pure and in-place on a copy the caller
        /// owns.
        /// </summary>
        public static void Straighten(float[] offsets)
        {
            if (offsets == null) throw new ArgumentNullException(nameof(offsets));

            int pixelCount = offsets.Length / 2;
            float spacing = MeasureSpacing(offsets);
            if (pixelCount < 2 || spacing <= 0f)
            {
                return;
            }

            float chordX = offsets[(pixelCount - 1) * 2] - offsets[0];
            float chordY = offsets[((pixelCount - 1) * 2) + 1] - offsets[1];
            float heading = chordX == 0f && chordY == 0f ? FirstLinkHeading(offsets) : HeadingDegrees(chordX, chordY);

            Relayout(offsets, heading, perPixelTurnDegrees: 0f, spacing);
        }

        /// <summary>
        /// Rewrites every pixel after the first as a walk from it: each link <paramref name="spacing"/>
        /// long, the first at <paramref name="startHeadingDegrees"/>, each subsequent one turned by
        /// <paramref name="perPixelTurnDegrees"/> more. The straight case is computed by
        /// multiplication rather than accumulation so a horizontal or vertical run is bit-exact, as
        /// <see cref="GenerateLine"/> is; an arc has to accumulate, and 720 float adds are accurate
        /// to well under a thousandth of an image pixel.
        /// </summary>
        private static void Relayout(float[] offsets, float startHeadingDegrees, float perPixelTurnDegrees, float spacing)
        {
            int pixelCount = offsets.Length / 2;
            float startX = offsets[0];
            float startY = offsets[1];

            if (perPixelTurnDegrees == 0f)
            {
                GetDirection(startHeadingDegrees, out float dx, out float dy);
                for (int i = 1; i < pixelCount; i++)
                {
                    offsets[i * 2] = startX + (i * spacing * dx);
                    offsets[(i * 2) + 1] = startY + (i * spacing * dy);
                }
                return;
            }

            float x = startX;
            float y = startY;
            for (int i = 1; i < pixelCount; i++)
            {
                GetDirection(startHeadingDegrees + ((i - 1) * perPixelTurnDegrees), out float dx, out float dy);
                x += spacing * dx;
                y += spacing * dy;
                offsets[i * 2] = x;
                offsets[(i * 2) + 1] = y;
            }
        }

        /// <summary>Heading of the first link with any length, or 0° (along +x) if there is none.</summary>
        private static float FirstLinkHeading(float[] offsets)
        {
            int pixelCount = offsets.Length / 2;
            for (int i = 1; i < pixelCount; i++)
            {
                float dx = offsets[i * 2] - offsets[(i - 1) * 2];
                float dy = offsets[(i * 2) + 1] - offsets[((i - 1) * 2) + 1];
                if (dx != 0f || dy != 0f)
                {
                    return HeadingDegrees(dx, dy);
                }
            }
            return 0f;
        }

        /// <summary>Direction of a vector in degrees, in [0, 360), with the quarter turns exact.</summary>
        public static float HeadingDegrees(float dx, float dy)
        {
            if (dy == 0f)
            {
                return dx >= 0f ? 0f : 180f;
            }
            if (dx == 0f)
            {
                return dy > 0f ? 90f : 270f;
            }

            float degrees = MathF.Atan2(dy, dx) * (180f / MathF.PI);
            if (degrees < 0f)
            {
                degrees += 360f;
            }
            return degrees;
        }

        /// <summary>A difference of headings folded into (-180, 180], so the turn is the short way round.</summary>
        private static float NormalizeTurn(float degrees)
        {
            degrees %= 360f;
            if (degrees > 180f)
            {
                degrees -= 360f;
            }
            else if (degrees <= -180f)
            {
                degrees += 360f;
            }
            return degrees;
        }

        /// <summary>
        /// Restores the convention that the origin marks pixel 0: shifts every offset so the first
        /// is (0,0) again and returns the shift, which is what the caller must add to the origin to
        /// leave every pixel exactly where it was. Zero when pixel 0 is already home, or the
        /// fixture is empty.
        /// </summary>
        public static PointF Rebase(float[] offsets)
        {
            if (offsets == null) throw new ArgumentNullException(nameof(offsets));
            if (offsets.Length % 2 != 0)
            {
                throw new ArgumentException("Offsets must hold an x and a y per pixel.", nameof(offsets));
            }
            if (offsets.Length == 0)
            {
                return default;
            }

            float shiftX = offsets[0];
            float shiftY = offsets[1];
            if (shiftX == 0f && shiftY == 0f)
            {
                return default;
            }

            for (int i = 0; i < offsets.Length; i += 2)
            {
                offsets[i] -= shiftX;
                offsets[i + 1] -= shiftY;
            }

            return new PointF(shiftX, shiftY);
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
