using Offtrack.Core;
using UnityEngine;
using UnityEngine.UI;

namespace Offtrack.Runtime.Presentation
{
    // Canvas primitives use their original top-left origin, including alphabetic text baselines.
    // Only the cutscenes use ordinary alpha blending; the Main palette material is untouched.
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class CutsceneGraphic : MaskableGraphic
    {
        private CutsceneCommand command;
        private Font labelFont;
        private int circleSegments = 192;

        public override Texture mainTexture => labelFont != null ? labelFont.material.mainTexture : Texture2D.whiteTexture;

        protected override void OnEnable()
        {
            base.OnEnable();
            Font.textureRebuilt += FontRebuilt;
        }

        protected override void OnDisable()
        {
            Font.textureRebuilt -= FontRebuilt;
            base.OnDisable();
        }

        private void FontRebuilt(Font changed)
        {
            if (changed != labelFont) return;
            SetVerticesDirty();
            SetMaterialDirty();
        }

        public void SetCommand(CutsceneCommand value, int segments)
        {
            command = value;
            circleSegments = segments;
            labelFont = value.Primitive == CutscenePrimitive.Label ? (value.Serif ? FontProvider.GetSerif() : FontProvider.Get()) : null;
            if (labelFont != null)
                labelFont.RequestCharactersInTexture(value.Text, value.FontSize, value.Serif ? FontStyle.Italic : FontStyle.Normal);
            var ink = value.Ink == CutsceneInk.Ink ? Palette.Ink : value.Ink == CutsceneInk.Red ? Palette.Red
                : value.Ink == CutsceneInk.Bone ? Palette.Bone : Palette.DarkRed;
            var tint = (Color)ink;
            tint.a = (float)value.Alpha;
            color = tint;
            SetVerticesDirty();
            SetMaterialDirty();
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            if (command.Alpha <= 0) return;
            var x = (float)command.X; var y = (float)command.Y;
            var w = (float)command.Width; var h = (float)command.Height;
            var stroke = (float)command.Stroke;
            switch (command.Primitive)
            {
                case CutscenePrimitive.Rectangle: Rectangle(vh, x, y, w, h); break;
                case CutscenePrimitive.Outline:
                    Rectangle(vh, x, y, w, stroke);
                    Rectangle(vh, x, y + h - stroke, w, stroke);
                    Rectangle(vh, x, y + stroke, stroke, h - 2 * stroke);
                    Rectangle(vh, x + w - stroke, y + stroke, stroke, h - 2 * stroke);
                    break;
                case CutscenePrimitive.Circle: Circle(vh, x, y, w, 0); break;
                case CutscenePrimitive.Ring: Circle(vh, x, y, w + stroke / 2, w - stroke / 2); break;
                case CutscenePrimitive.Line:
                    var a = new Vector2(x, -y); var b = new Vector2(w, -h);
                    var perpendicular = new Vector2(-(b - a).y, (b - a).x).normalized * stroke / 2;
                    Quad(vh, a - perpendicular, a + perpendicular, b + perpendicular, b - perpendicular);
                    break;
                case CutscenePrimitive.Label: Label(vh); break;
            }
        }

        private void Rectangle(VertexHelper vh, float x, float y, float w, float h)
        {
            if (w <= 0 || h <= 0) return;
            Quad(vh, new Vector2(x, -y), new Vector2(x + w, -y), new Vector2(x + w, -y - h), new Vector2(x, -y - h));
        }

        private void Circle(VertexHelper vh, float x, float y, float outer, float inner)
        {
            if (outer <= 0) return;
            var center = new Vector2(x, -y);
            var n = Mathf.Max(12, circleSegments);
            if (inner <= 0)
            {
                vh.AddVert(center, color, Vector2.zero);
                for (var i = 0; i <= n; i++)
                {
                    var angle = i * Mathf.PI * 2 / n;
                    vh.AddVert(center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * outer, color, Vector2.zero);
                }
                for (var i = 0; i < n; i++) vh.AddTriangle(0, i + 1, i + 2);
            }
            else
            {
                for (var i = 0; i <= n; i++)
                {
                    var angle = i * Mathf.PI * 2 / n;
                    var u = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                    vh.AddVert(center + u * outer, color, Vector2.zero);
                    vh.AddVert(center + u * inner, color, Vector2.zero);
                }
                for (var i = 0; i < n; i++)
                {
                    var index = i * 2;
                    vh.AddTriangle(index, index + 1, index + 3);
                    vh.AddTriangle(index, index + 3, index + 2);
                }
            }
        }

        private void Quad(VertexHelper vh, Vector2 a, Vector2 b, Vector2 c, Vector2 d)
        {
            var start = vh.currentVertCount;
            vh.AddVert(a, color, Vector2.zero); vh.AddVert(b, color, Vector2.zero);
            vh.AddVert(c, color, Vector2.zero); vh.AddVert(d, color, Vector2.zero);
            vh.AddTriangle(start, start + 1, start + 2); vh.AddTriangle(start, start + 2, start + 3);
        }

        private void Label(VertexHelper vh)
        {
            if (labelFont == null || string.IsNullOrEmpty(command.Text)) return;
            var style = command.Serif ? FontStyle.Italic : FontStyle.Normal;
            var advance = 0f;
            foreach (var ch in command.Text)
                if (labelFont.GetCharacterInfo(ch, out var info, command.FontSize, style)) advance += info.advance;
            var scale = (float)command.Scale;
            var cursor = (float)command.X - advance * scale * (command.Align == CutsceneTextAlign.Center ? .5f : command.Align == CutsceneTextAlign.Right ? 1 : 0);
            var baseline = -(float)command.Y;
            foreach (var ch in command.Text)
            {
                if (!labelFont.GetCharacterInfo(ch, out var info, command.FontSize, style)) continue;
                var left = cursor + info.minX * scale; var right = cursor + info.maxX * scale;
                var bottom = baseline + info.minY * scale; var top = baseline + info.maxY * scale;
                if (right > left && top > bottom)
                {
                    var clipLeft = left; var clipRight = right; var clipBottom = bottom; var clipTop = top;
                    if (command.HasClip)
                    {
                        clipLeft = Mathf.Max(left, (float)command.ClipX);
                        clipRight = Mathf.Min(right, (float)(command.ClipX + command.ClipWidth));
                        clipBottom = Mathf.Max(bottom, -(float)(command.ClipY + command.ClipHeight));
                        clipTop = Mathf.Min(top, -(float)command.ClipY);
                    }
                    if (clipRight > clipLeft && clipTop > clipBottom)
                    {
                        var u0 = (clipLeft - left) / (right - left); var u1 = (clipRight - left) / (right - left);
                        var v0 = (clipBottom - bottom) / (top - bottom); var v1 = (clipTop - bottom) / (top - bottom);
                        var start = vh.currentVertCount;
                        vh.AddVert(new Vector2(clipLeft, clipBottom), color, GlyphUv(info, u0, v0));
                        vh.AddVert(new Vector2(clipRight, clipBottom), color, GlyphUv(info, u1, v0));
                        vh.AddVert(new Vector2(clipRight, clipTop), color, GlyphUv(info, u1, v1));
                        vh.AddVert(new Vector2(clipLeft, clipTop), color, GlyphUv(info, u0, v1));
                        vh.AddTriangle(start, start + 1, start + 2); vh.AddTriangle(start, start + 2, start + 3);
                    }
                }
                cursor += info.advance * scale;
            }
        }

        private static Vector2 GlyphUv(CharacterInfo info, float x, float y) => Vector2.Lerp(
            Vector2.Lerp(info.uvBottomLeft, info.uvBottomRight, x), Vector2.Lerp(info.uvTopLeft, info.uvTopRight, x), y);
    }
}
