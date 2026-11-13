using Offtrack.Core;
using UnityEngine;
using UnityEngine.UI;

namespace Offtrack.Runtime.Presentation
{
    // Authored coordinates have a top-left origin and text uses an alphabetic baseline.
    // Camera transforms and viewport clipping apply equally to every shape and glyph.
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class CutsceneGraphic : MaskableGraphic
    {
        private CutsceneCommand command;
        private Font labelFont;
        private int circleSegments = 192;
        private float cameraScale = 1, cameraOffsetX, cameraOffsetY, visibility = 1;
        private readonly MeshPoint[] polygonA = new MeshPoint[12];
        private readonly MeshPoint[] polygonB = new MeshPoint[12];

        private struct MeshPoint
        {
            public Vector2 Position, Uv;
            public MeshPoint(Vector2 position, Vector2 uv) { Position = position; Uv = uv; }
        }

        public override Texture mainTexture => command.Primitive == CutscenePrimitive.Label && labelFont != null
            ? labelFont.material.mainTexture : Texture2D.whiteTexture;

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

        public void SetCommand(CutsceneCommand value, int segments, double cameraScale = 1,
            double offsetX = 0, double offsetY = 0, double opacity = 1)
        {
            command = value;
            circleSegments = segments;
            this.cameraScale = (float)cameraScale;
            cameraOffsetX = (float)offsetX;
            cameraOffsetY = (float)offsetY;
            visibility = Mathf.Clamp01((float)(value.Alpha * opacity));
            labelFont = value.Primitive == CutscenePrimitive.Label || value.Primitive == CutscenePrimitive.ThoughtBubble
                ? (value.Serif ? FontProvider.GetSerif() : FontProvider.Get()) : null;
            if (labelFont != null)
                labelFont.RequestCharactersInTexture(value.Text, value.FontSize, value.Serif ? FontStyle.Italic : FontStyle.Normal);
            color = ResolveInk(value.Ink, visibility);
            SetVerticesDirty();
            SetMaterialDirty();
        }

        // Every fade is a finite sequence of named opaque Palette colors. Shade strength and
        // logical visibility multiply before quantization, so an already-dim board stays dim.
        public static Color32 ResolveInk(CutsceneInk ink, double opacity = 1)
        {
            if (ink == CutsceneInk.Ink) return Palette.Ink;
            var strength = ink == CutsceneInk.DarkRed60 || ink == CutsceneInk.Bone60 || ink == CutsceneInk.Red60 ? .6
                : ink == CutsceneInk.DarkRed40 || ink == CutsceneInk.Bone40 || ink == CutsceneInk.Red40 ? .4
                : ink == CutsceneInk.DarkRed15 || ink == CutsceneInk.Bone15 || ink == CutsceneInk.Red15 ? .15 : 1;
            strength *= CutsceneTimeline.Clamp(opacity, 0, 1);
            if (strength < .075) return Palette.Ink;
            var step = strength < .275 ? 15 : strength < .5 ? 40 : strength < .8 ? 60 : 100;
            if (ink == CutsceneInk.Bone || ink == CutsceneInk.Bone60 || ink == CutsceneInk.Bone40 || ink == CutsceneInk.Bone15)
                return step == 15 ? Palette.Bone15 : step == 40 ? Palette.Bone40 : step == 60 ? Palette.Bone60 : Palette.Bone;
            if (ink == CutsceneInk.Red || ink == CutsceneInk.Red60 || ink == CutsceneInk.Red40 || ink == CutsceneInk.Red15)
                return step == 15 ? Palette.Red15 : step == 40 ? Palette.Red40 : step == 60 ? Palette.Red60 : Palette.Red;
            return step == 15 ? Palette.DarkRed15 : step == 40 ? Palette.DarkRed40 : step == 60 ? Palette.DarkRed60 : Palette.DarkRed;
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            if (visibility <= 0) return;
            var x = (float)command.X; var y = (float)command.Y;
            var w = (float)command.Width; var h = (float)command.Height;
            var stroke = (float)command.Stroke;
            switch (command.Primitive)
            {
                case CutscenePrimitive.Rectangle: Rectangle(vh, x, y, w, h); break;
                case CutscenePrimitive.RotatedRectangle: RotatedRectangle(vh, x, y, w, h, (float)command.Rotation); break;
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
                case CutscenePrimitive.ThoughtBubble:
                    var scale = (float)command.Scale;
                    w = (MeasureText() + 32) * scale;
                    h *= scale;
                    stroke *= scale;
                    x -= w / 2;
                    y -= 8 * scale + h / 2;
                    Rectangle(vh, x, y, w, stroke);
                    Rectangle(vh, x, y + h - stroke, w, stroke);
                    Rectangle(vh, x, y + stroke, stroke, h - 2 * stroke);
                    Rectangle(vh, x + w - stroke, y + stroke, stroke, h - 2 * stroke);
                    break;
            }
        }

        private void Rectangle(VertexHelper vh, float x, float y, float w, float h)
        {
            if (w <= 0 || h <= 0) return;
            Quad(vh, new Vector2(x, -y), new Vector2(x + w, -y), new Vector2(x + w, -y - h), new Vector2(x, -y - h));
        }

        private void RotatedRectangle(VertexHelper vh, float x, float y, float w, float h, float degrees)
        {
            if (w <= 0 || h <= 0) return;
            var center = new Vector2(x + w / 2, -y - h / 2);
            var radians = -degrees * Mathf.Deg2Rad;
            var horizontal = new Vector2(Mathf.Cos(radians), Mathf.Sin(radians)) * w / 2;
            var vertical = new Vector2(-Mathf.Sin(radians), Mathf.Cos(radians)) * h / 2;
            Quad(vh, center - horizontal + vertical, center + horizontal + vertical,
                center + horizontal - vertical, center - horizontal - vertical);
        }

        private void Circle(VertexHelper vh, float x, float y, float outer, float inner)
        {
            if (outer <= 0) return;
            var center = new Vector2(x, -y);
            var n = Mathf.Max(12, circleSegments);
            if (inner <= 0)
            {
                for (var i = 0; i < n; i++)
                {
                    var angle = i * Mathf.PI * 2 / n;
                    var nextAngle = (i + 1) * Mathf.PI * 2 / n;
                    polygonA[0] = Point(center);
                    polygonA[1] = Point(center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * outer);
                    polygonA[2] = Point(center + new Vector2(Mathf.Cos(nextAngle), Mathf.Sin(nextAngle)) * outer);
                    Polygon(vh, 3);
                }
            }
            else
            {
                for (var i = 0; i < n; i++)
                {
                    var angle = i * Mathf.PI * 2 / n;
                    var nextAngle = (i + 1) * Mathf.PI * 2 / n;
                    var u = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                    var v = new Vector2(Mathf.Cos(nextAngle), Mathf.Sin(nextAngle));
                    Quad(vh, center + u * outer, center + u * inner, center + v * inner, center + v * outer);
                }
            }
        }

        private void Quad(VertexHelper vh, Vector2 a, Vector2 b, Vector2 c, Vector2 d)
        {
            polygonA[0] = Point(a); polygonA[1] = Point(b);
            polygonA[2] = Point(c); polygonA[3] = Point(d);
            Polygon(vh, 4);
        }

        private MeshPoint Point(Vector2 position, Vector2 uv = default)
            => new MeshPoint(new Vector2(position.x * cameraScale + cameraOffsetX,
                position.y * cameraScale - cameraOffsetY), uv);

        // Clip in the fixed frame after applying the camera. This also protects portrait
        // letterboxes when using the existing flat material, which has no RectMask shader pass.
        private void Polygon(VertexHelper vh, int count)
        {
            var input = polygonA;
            var output = polygonB;
            for (var side = 0; side < 4 && count > 0; side++)
            {
                int written = 0;
                var previous = input[count - 1];
                var previousDistance = EdgeDistance(previous.Position, side);
                for (var i = 0; i < count; i++)
                {
                    var current = input[i];
                    var distance = EdgeDistance(current.Position, side);
                    if ((distance >= 0) != (previousDistance >= 0))
                    {
                        var ratio = previousDistance / (previousDistance - distance);
                        output[written++] = new MeshPoint(Vector2.LerpUnclamped(previous.Position, current.Position, ratio),
                            Vector2.LerpUnclamped(previous.Uv, current.Uv, ratio));
                    }
                    if (distance >= 0) output[written++] = current;
                    previous = current;
                    previousDistance = distance;
                }
                count = written;
                var swap = input; input = output; output = swap;
            }
            if (count < 3) return;
            var start = vh.currentVertCount;
            for (var i = 0; i < count; i++) vh.AddVert(input[i].Position, color, input[i].Uv);
            for (var i = 1; i < count - 1; i++) vh.AddTriangle(start, start + i, start + i + 1);
        }

        private static float EdgeDistance(Vector2 position, int side)
            => side == 0 ? position.x : side == 1 ? 1600 - position.x : side == 2 ? -position.y : 900 + position.y;

        private float MeasureText()
        {
            if (labelFont == null || string.IsNullOrEmpty(command.Text)) return 0;
            var style = command.Serif ? FontStyle.Italic : FontStyle.Normal;
            float width = 0;
            foreach (var ch in command.Text)
                if (labelFont.GetCharacterInfo(ch, out var info, command.FontSize, style)) width += info.advance;
            return width;
        }

        private void Label(VertexHelper vh)
        {
            if (labelFont == null || string.IsNullOrEmpty(command.Text)) return;
            var style = command.Serif ? FontStyle.Italic : FontStyle.Normal;
            var advance = MeasureText();
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
                        polygonA[0] = Point(new Vector2(clipLeft, clipBottom), GlyphUv(info, u0, v0));
                        polygonA[1] = Point(new Vector2(clipRight, clipBottom), GlyphUv(info, u1, v0));
                        polygonA[2] = Point(new Vector2(clipRight, clipTop), GlyphUv(info, u1, v1));
                        polygonA[3] = Point(new Vector2(clipLeft, clipTop), GlyphUv(info, u0, v1));
                        Polygon(vh, 4);
                    }
                }
                cursor += info.advance * scale;
            }
        }

        private static Vector2 GlyphUv(CharacterInfo info, float x, float y) => Vector2.Lerp(
            Vector2.Lerp(info.uvBottomLeft, info.uvBottomRight, x), Vector2.Lerp(info.uvTopLeft, info.uvTopRight, x), y);
    }
}
