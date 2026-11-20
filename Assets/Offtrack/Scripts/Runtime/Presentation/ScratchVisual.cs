using UnityEngine;

namespace Offtrack.Runtime.Presentation
{
    // Three pooled silhouettes share an opaque ink incision; only the blade strokes change color.
    public sealed class ScratchVisual : MonoBehaviour
    {
        [SerializeField] private FlatShape cut;
        [SerializeField] private FlatShape cross;
        [SerializeField] private FlatShape second;
        [SerializeField] private FlatShape notch;
        private FlatShape runner, gap, capStart, capEnd;
        public int Variant { get; private set; }
        public bool Highlighted { get; private set; }
        public float Growth { get; private set; }

        public void Configure(FlatShape mainCut, FlatShape crossingCut, FlatShape secondCut, FlatShape inkNotch)
        {
            cut = mainCut; cross = crossingCut; second = secondCut; notch = inkNotch;
        }

        public void Present(int noteId, bool highlighted, float growth, ViewSettings view, int runnerDirection = 0)
        {
            Variant = noteId % 3;
            Highlighted = highlighted;
            Growth = Mathf.Clamp01(growth);
            cut.gameObject.SetActive(true);
            notch.gameObject.SetActive(true);
            SetGapActive(false);
            cross.gameObject.SetActive(Variant == 1);
            second.gameObject.SetActive(Variant == 2);
            var ink = highlighted ? Palette.Danger : Palette.Danger60;
            cut.color = cross.color = second.color = ink;
            notch.color = Palette.Ink;
            var width = highlighted ? view.ScratchHighlightWidth : view.ScratchSize.y;
            cut.rectTransform.sizeDelta = new Vector2(view.ScratchSize.x, width);
            cross.rectTransform.sizeDelta = new Vector2(view.ScratchCrossSize.x,
                highlighted ? view.ScratchCrossSize.y * view.ScratchHighlightWidth / view.ScratchSize.y : view.ScratchCrossSize.y);
            second.rectTransform.sizeDelta = new Vector2(view.ScratchCrossSize.x, width);
            transform.localScale = new Vector3(Growth, 1, 1);
            if (runnerDirection != 0)
            {
                runner ??= Shape("RunnerTail", ShapeKind.Triangle, ink);
                runner.gameObject.SetActive(true);
                runner.color = ink;
                runner.rectTransform.sizeDelta = view.RunnerTailSize;
                runner.rectTransform.anchoredPosition = Vector2.left * runnerDirection * (view.ScratchSize.x + view.RunnerTailSize.y) / 2;
                runner.rectTransform.localRotation = Quaternion.Euler(0, 0, runnerDirection > 0 ? 180 : 0);
            }
            else if (runner != null) runner.gameObject.SetActive(false);
        }

        public void PresentGap(float radius, float angle, float sweep, bool highlighted, float growth, ViewSettings view)
        {
            if (gap == null)
            {
                gap = Shape("MissingLaneArc", ShapeKind.Arc, Palette.Ink);
                capStart = Shape("BreakStart", ShapeKind.Rectangle, Palette.Danger);
                capEnd = Shape("BreakEnd", ShapeKind.Rectangle, Palette.Danger);
            }
            cut.gameObject.SetActive(false);
            cross.gameObject.SetActive(false);
            second.gameObject.SetActive(false);
            notch.gameObject.SetActive(false);
            if (runner != null) runner.gameObject.SetActive(false);
            SetGapActive(true);
            transform.localScale = Vector3.one;
            Growth = Mathf.Clamp01(growth);
            Highlighted = highlighted;
            gap.StrokeWidth = view.LaneBandWidth;
            gap.rectTransform.sizeDelta = Vector2.one * (radius * 2 + view.LaneBandWidth);
            gap.ArcStart = 90 + angle;
            gap.ArcSweep = sweep;
            gap.SetVerticesDirty();
            Cap(capStart, angle);
            Cap(capEnd, angle + sweep);

            void Cap(FlatShape cap, float degrees)
            {
                cap.color = highlighted ? Palette.Danger : Palette.Danger60;
                cap.rectTransform.sizeDelta = new Vector2(view.BreakCapSize.x, view.BreakCapSize.y * Growth);
                cap.rectTransform.anchoredPosition = ScratchPool.Point(degrees) * radius;
                cap.rectTransform.localRotation = Quaternion.Euler(0, 0, degrees);
            }
        }

        private void SetGapActive(bool active)
        {
            if (gap == null) return;
            gap.gameObject.SetActive(active);
            capStart.gameObject.SetActive(active);
            capEnd.gameObject.SetActive(active);
        }

        private FlatShape Shape(string name, ShapeKind kind, Color color)
        {
            var child = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer));
            child.transform.SetParent(transform, false);
            var shape = child.AddComponent<FlatShape>();
            shape.Kind = kind;
            shape.color = color;
            shape.raycastTarget = false;
            return shape;
        }
    }
}
