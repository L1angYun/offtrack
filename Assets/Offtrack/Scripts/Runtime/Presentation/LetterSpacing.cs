using UnityEngine;
using UnityEngine.UI;

namespace Offtrack.Runtime.Presentation
{
    // Tracking for short single-line display copy (dialog tag, buttons), like the prototype's
    // CSS letter-spacing. uGUI Text emits one quad per visible glyph; each quad moves right by
    // Spacing reference pixels per preceding glyph and the line is re-centred by its alignment.
    [RequireComponent(typeof(Text))]
    public sealed class LetterSpacing : BaseMeshEffect
    {
        public float Spacing;

        public override void ModifyMesh(VertexHelper vh)
        {
            if (!IsActive() || Mathf.Approximately(Spacing, 0)) return;
            var quads = vh.currentVertCount / 4;
            if (quads < 2) return;
            var label = GetComponent<Text>();
            var offsets = new float[quads];
            for (int quad = 1; quad < quads; quad++)
            {
                // Full-width punctuation already owns a full advance in CJK fonts. Tracking
                // either side of it makes 「再来一把！」 look like a separate exclamation button.
                bool touchesPunctuation = quad < label.text.Length &&
                    (IsFullWidthPunctuation(label.text[quad - 1]) || IsFullWidthPunctuation(label.text[quad]));
                offsets[quad] = offsets[quad - 1] + (touchesPunctuation ? 0 : Spacing);
            }
            var total = offsets[quads - 1];
            float shift;
            switch (label.alignment)
            {
                case TextAnchor.UpperCenter:
                case TextAnchor.MiddleCenter:
                case TextAnchor.LowerCenter:
                    shift = -total / 2; break;
                case TextAnchor.UpperRight:
                case TextAnchor.MiddleRight:
                case TextAnchor.LowerRight:
                    shift = -total; break;
                default:
                    shift = 0; break;
            }
            // Text already snapped its glyphs to whole device pixels (pixel-perfect canvas); keep each
            // offset a whole number of device pixels too, or the glyph atlas is sampled half a pixel off
            // and thin strokes blur.
            var scale = graphic.canvas != null ? graphic.canvas.scaleFactor : 1f;
            var vertex = new UIVertex();
            for (var quad = 0; quad < quads; quad++)
            {
                var offset = Mathf.Round((offsets[quad] + shift) * scale) / scale;
                for (var corner = 0; corner < 4; corner++)
                {
                    var index = quad * 4 + corner;
                    vh.PopulateUIVertex(ref vertex, index);
                    vertex.position.x += offset;
                    vh.SetUIVertex(vertex, index);
                }
            }
        }

        public static bool IsFullWidthPunctuation(char value) =>
            "！，。？；：、（）【】《》〈〉「」『』〔〕［］｛｝…".IndexOf(value) >= 0;
    }
}
