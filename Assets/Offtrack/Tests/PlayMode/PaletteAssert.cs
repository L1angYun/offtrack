using System.Collections.Generic;
using System.Text;
using NUnit.Framework;
using Offtrack.Runtime.Presentation;
using UnityEngine;

namespace Offtrack.Tests.PlayMode
{
    internal static class PaletteAssert
    {
        private const float Tolerance = 3f;
        private static readonly Vector3[] Colors =
        {
            ToVector(Palette.Ink), ToVector(Palette.Red), ToVector(Palette.Bone), ToVector(Palette.DarkRed)
        };

        public static int Rgb(Color32 color) => (color.r << 16) | (color.g << 8) | color.b;

        // The proof of the four-color rule. Run it on a render made with the OFFTRACK_STRICT_PALETTE
        // shader keyword and without MSAA: edges are hard there, so any fifth color, tinted overlay,
        // glow, shadow or gradient shows up as an RGB value outside the four.
        public static HashSet<int> AssertExactPalette(Color32[] pixels, string label)
        {
            var allowed = new HashSet<int> { Rgb(Palette.Ink), Rgb(Palette.Red), Rgb(Palette.Bone), Rgb(Palette.DarkRed) };
            var actual = new HashSet<int>();
            foreach (Color32 pixel in pixels) actual.Add(Rgb(pixel));
            CollectionAssert.IsSubsetOf(actual, allowed, label + " has pixels outside the four-color palette.");
            TestContext.WriteLine($"{label}: RGB colors={actual.Count}");
            return actual;
        }

        // Sanity check only, for the anti-aliased evidence render that players see: every pixel must be
        // a palette color or a mix of palette colors (inside their RGB hull, within 8-bit rounding), and
        // flat fills must dominate. Mixes such as a dim overlay or a glow would also pass this check;
        // AssertExactPalette on the strict render is what rules those out.
        public static void AssertWithinPaletteHull(Color32[] pixels, string label, double minimumExactShare = 0.85)
        {
            var exactColors = new HashSet<int> { Rgb(Palette.Ink), Rgb(Palette.Red), Rgb(Palette.Bone), Rgb(Palette.DarkRed) };
            var verdicts = new Dictionary<int, bool>();
            var unique = new HashSet<int>();
            var failures = new StringBuilder();
            int failureCount = 0;
            long exact = 0;
            foreach (Color32 pixel in pixels)
            {
                int key = Rgb(pixel);
                unique.Add(key);
                if (exactColors.Contains(key)) { exact++; continue; }
                if (!verdicts.TryGetValue(key, out bool ok))
                {
                    ok = InsideHull(new Vector3(pixel.r, pixel.g, pixel.b));
                    verdicts[key] = ok;
                    if (!ok && failureCount++ < 8) failures.Append($" #{key:X6}");
                }
            }
            Assert.That(failureCount, Is.Zero, $"{label}: colors that are not mixes of the four palette colors:{failures}");
            double share = (double)exact / pixels.Length;
            Assert.That(share, Is.GreaterThanOrEqualTo(minimumExactShare),
                $"{label}: only {share:P1} of pixels are flat palette colors.");
            TestContext.WriteLine($"{label}: RGB colors={unique.Count}, exact palette pixels={share:P2}");
        }

        // The hull of four points is covered by its four triangular faces plus the interior; the palette
        // tetrahedron is so thin that every interior point lies within Tolerance of a face.
        private static bool InsideHull(Vector3 point)
        {
            for (int a = 0; a < Colors.Length; a++)
                for (int b = a + 1; b < Colors.Length; b++)
                    for (int c = b + 1; c < Colors.Length; c++)
                        if ((ClosestPointOnTriangle(point, Colors[a], Colors[b], Colors[c]) - point).magnitude <= Tolerance)
                            return true;
            return false;
        }

        // Real-Time Collision Detection (Ericson), section 5.1.5.
        private static Vector3 ClosestPointOnTriangle(Vector3 p, Vector3 a, Vector3 b, Vector3 c)
        {
            Vector3 ab = b - a, ac = c - a, ap = p - a;
            float d1 = Vector3.Dot(ab, ap), d2 = Vector3.Dot(ac, ap);
            if (d1 <= 0 && d2 <= 0) return a;
            Vector3 bp = p - b;
            float d3 = Vector3.Dot(ab, bp), d4 = Vector3.Dot(ac, bp);
            if (d3 >= 0 && d4 <= d3) return b;
            float vc = d1 * d4 - d3 * d2;
            if (vc <= 0 && d1 >= 0 && d3 <= 0) return a + d1 / (d1 - d3) * ab;
            Vector3 cp = p - c;
            float d5 = Vector3.Dot(ab, cp), d6 = Vector3.Dot(ac, cp);
            if (d6 >= 0 && d5 <= d6) return c;
            float vb = d5 * d2 - d1 * d6;
            if (vb <= 0 && d2 >= 0 && d6 <= 0) return a + d2 / (d2 - d6) * ac;
            float va = d3 * d6 - d5 * d4;
            if (va <= 0 && d4 - d3 >= 0 && d5 - d6 >= 0) return b + (d4 - d3) / (d4 - d3 + d5 - d6) * (c - b);
            float denominator = 1f / (va + vb + vc);
            return a + ab * (vb * denominator) + ac * (vc * denominator);
        }

        private static Vector3 ToVector(Color32 color) => new Vector3(color.r, color.g, color.b);
    }
}
