using System.Collections.Generic;
using UnityEngine;

namespace Offtrack.Runtime.Presentation
{
    // 全项目唯一的颜色定义；材质和几何只引用这里。
    public static class Palette
    {
        public static readonly Color32 Ink = new Color32(0x0B, 0x07, 0x07, 255);
        public static readonly Color32 Red = new Color32(0xEC, 0x41, 0x41, 255);
        public static readonly Color32 Bone = new Color32(0xF0, 0xE6, 0xD6, 255);
        public static readonly Color32 DarkRed = new Color32(0x8C, 0x3A, 0x3A, 255);

        // Fixed, opaque sRGB composites over Ink. These are steps, never live opacity effects.
        public static readonly Color32 DarkRed60 = new Color32(0x58, 0x26, 0x26, 255);
        public static readonly Color32 DarkRed40 = new Color32(0x3F, 0x1B, 0x1B, 255);
        public static readonly Color32 DarkRed15 = new Color32(0x1E, 0x0F, 0x0F, 255);
        public static readonly Color32 Bone60 = new Color32(0x94, 0x8D, 0x83, 255);
        public static readonly Color32 Bone40 = new Color32(0x67, 0x60, 0x5A, 255);
        public static readonly Color32 Bone15 = new Color32(0x2D, 0x28, 0x26, 255);
        public static readonly Color32 Red60 = new Color32(0x92, 0x2A, 0x2A, 255);
        public static readonly Color32 Red40 = new Color32(0x65, 0x1E, 0x1E, 255);
        public static readonly Color32 Red15 = new Color32(0x2D, 0x10, 0x10, 255);

        // The only translucent Main graphic: one instantaneous full-phone dimmer (219/255).
        public static readonly Color32 DialogDim = new Color32(Ink.r, Ink.g, Ink.b, 219);

        public static IReadOnlyList<Color32> BaseColors { get; } = System.Array.AsReadOnly(new[]
        {
            Ink, Red, Bone, DarkRed
        });

        public static IReadOnlyList<Color32> OpaqueColors { get; } = System.Array.AsReadOnly(new[]
        {
            Ink, Red, Bone, DarkRed, DarkRed60, DarkRed40, DarkRed15, Bone60, Bone40, Bone15,
            Red60, Red40, Red15
        });

        // Rendering and acceptance share this single whitelist, including exactly one dimmer layer.
        public static IReadOnlyList<Color32> AllowedColors { get; } = BuildWhitelist();

        public static Color32 Dimmed(Color32 underlying) => new Color32(
            CompositeChannel(underlying.r, DialogDim.r), CompositeChannel(underlying.g, DialogDim.g),
            CompositeChannel(underlying.b, DialogDim.b), 255);

        private static byte CompositeChannel(byte background, byte foreground) =>
            (byte)((background * (255 - DialogDim.a) + foreground * DialogDim.a + 127) / 255);

        private static IReadOnlyList<Color32> BuildWhitelist()
        {
            var colors = new List<Color32>(OpaqueColors);
            foreach (var color in OpaqueColors)
            {
                var dimmed = Dimmed(color);
                if (!colors.Contains(dimmed)) colors.Add(dimmed);
            }
            return colors.AsReadOnly();
        }
    }
}
