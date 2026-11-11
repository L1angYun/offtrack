using UnityEngine;
using UnityEngine.UI;

namespace Offtrack.Runtime.Presentation
{
    public enum ShapeKind { Rectangle, Disc, Ring, Triangle }

    // 不依赖贴图的平涂几何。所有顶点均使用一个不透明色。
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class FlatShape : MaskableGraphic
    {
        public ShapeKind Kind;
        public float StrokeWidth = 2;
        public int Segments = 192;

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            var r = rectTransform.rect;
            if (Kind == ShapeKind.Rectangle)
            {
                AddQuad(vh, new Vector2(r.xMin, r.yMin), new Vector2(r.xMin, r.yMax),
                    new Vector2(r.xMax, r.yMax), new Vector2(r.xMax, r.yMin));
                return;
            }
            if (Kind == ShapeKind.Triangle)
            {
                AddVertex(vh, new Vector2(r.xMin, r.yMin));
                AddVertex(vh, new Vector2(r.xMin, r.yMax));
                AddVertex(vh, new Vector2(r.xMax, r.center.y));
                vh.AddTriangle(0, 1, 2);
                return;
            }
            var radius = Mathf.Min(r.width, r.height) / 2;
            var count = Mathf.Max(3, Segments);
            if (Kind == ShapeKind.Disc)
            {
                AddVertex(vh, r.center);
                for (var i = 0; i <= count; i++)
                    AddVertex(vh, r.center + Unit(i, count) * radius);
                for (var i = 0; i < count; i++) vh.AddTriangle(0, i + 2, i + 1);
            }
            else
            {
                var inner = Mathf.Max(0, radius - StrokeWidth);
                for (var i = 0; i <= count; i++)
                {
                    var unit = Unit(i, count);
                    AddVertex(vh, r.center + unit * radius);
                    AddVertex(vh, r.center + unit * inner);
                }
                for (var i = 0; i < count; i++)
                {
                    var a = i * 2;
                    vh.AddTriangle(a, a + 3, a + 1);
                    vh.AddTriangle(a, a + 2, a + 3);
                }
            }
        }

        private static Vector2 Unit(int index, int count)
        {
            var angle = index * Mathf.PI * 2 / count;
            return new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
        }

        private void AddVertex(VertexHelper vh, Vector2 point) => vh.AddVert(point, color, Vector2.zero);

        private void AddQuad(VertexHelper vh, Vector2 a, Vector2 b, Vector2 c, Vector2 d)
        {
            AddVertex(vh, a); AddVertex(vh, b); AddVertex(vh, c); AddVertex(vh, d);
            vh.AddTriangle(0, 1, 2); vh.AddTriangle(2, 3, 0);
        }
    }
}
