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
    }
}
