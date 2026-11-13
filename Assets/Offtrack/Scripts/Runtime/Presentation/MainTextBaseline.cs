using UnityEngine;
using UnityEngine.UI;

namespace Offtrack.Runtime.Presentation
{
    // YaHei's line box sits visibly below the geometric centre. Offset only Main's rendered
    // glyphs, keeping RectTransforms (including button hit areas) and cutscene typography intact.
    [RequireComponent(typeof(Text))]
    public sealed class MainTextBaseline : BaseMeshEffect
    {
        public float OffsetEm;

        public override void ModifyMesh(VertexHelper vh)
        {
            if (!IsActive() || FontProvider.SelectedName != "Microsoft YaHei") return;
            var label = GetComponent<Text>();
            // The offset corrects YaHei's line box only; machine print uses its own metrics.
            if (label.font != null && label.font != FontProvider.Get()) return;
            float scale = graphic.canvas != null ? graphic.canvas.scaleFactor : 1f;
            float offset = Mathf.Round(label.fontSize * OffsetEm * scale) / scale;
            var vertex = new UIVertex();
            for (int i = 0; i < vh.currentVertCount; i++)
            {
                vh.PopulateUIVertex(ref vertex, i);
                vertex.position.y += offset;
                vh.SetUIVertex(vertex, i);
            }
        }
    }
}
