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
            var total = (quads - 1) * Spacing;
            float shift;
            switch (GetComponent<Text>().alignment)
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
                var offset = Mathf.Round((quad * Spacing + shift) * scale) / scale;
                for (var corner = 0; corner < 4; corner++)
                {
                    var index = quad * 4 + corner;
                    vh.PopulateUIVertex(ref vertex, index);
                    vertex.position.x += offset;
                    vh.SetUIVertex(vertex, index);
                }
            }
        }
    }
}
