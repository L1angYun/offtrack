using UnityEngine;
using UnityEngine.UI;

namespace Offtrack.Runtime.Presentation
{
    /// <summary>A non-interactive red wash with a clear center, generated without a texture.</summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class DamageVignette : MaskableGraphic
    {
        private float strength, edgeOpacity, centerOpacity;
        public float Strength => strength;
        public float EdgeAlpha => strength * edgeOpacity;
        public float CenterAlpha => strength * centerOpacity;

        public void SetFeedback(float value, DamageFeedbackSettings settings)
        {
            value = Mathf.Clamp01(value);
            if (strength == value && edgeOpacity == settings.EdgeOpacity && centerOpacity == settings.CenterOpacity) return;
            strength = value; edgeOpacity = settings.EdgeOpacity; centerOpacity = settings.CenterOpacity;
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper vertices)
        {
            vertices.Clear();
            if (strength <= 0) return;
            const int columns = 24, rows = 40;
            Rect rect = rectTransform.rect;
            for (int y = 0; y <= rows; y++)
            for (int x = 0; x <= columns; x++)
            {
                float u = x / (float)columns, v = y / (float)rows;
                float dx = 2 * u - 1, dy = 2 * v - 1;
                float edge = Mathf.Clamp01(dx * dx + dy * dy);
                edge *= edge;
                Color tint = color;
                tint.a *= strength * Mathf.Lerp(centerOpacity, edgeOpacity, edge);
                vertices.AddVert(new Vector2(Mathf.Lerp(rect.xMin, rect.xMax, u), Mathf.Lerp(rect.yMin, rect.yMax, v)), tint, Vector2.zero);
            }
            for (int y = 0; y < rows; y++)
            for (int x = 0; x < columns; x++)
            {
                int a = y * (columns + 1) + x, b = a + columns + 1;
                vertices.AddTriangle(a, b, a + 1);
                vertices.AddTriangle(a + 1, b, b + 1);
            }
        }
    }
}
