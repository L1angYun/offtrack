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
        public int Variant { get; private set; }
        public bool Highlighted { get; private set; }
        public float Growth { get; private set; }

        public void Configure(FlatShape mainCut, FlatShape crossingCut, FlatShape secondCut, FlatShape inkNotch)
        {
            cut = mainCut; cross = crossingCut; second = secondCut; notch = inkNotch;
        }

        public void Present(int noteId, bool highlighted, float growth, ViewSettings view)
        {
            Variant = noteId % 3;
            Highlighted = highlighted;
            Growth = Mathf.Clamp01(growth);
            cross.gameObject.SetActive(Variant == 1);
            second.gameObject.SetActive(Variant == 2);
            var ink = highlighted ? Palette.Red : Palette.DarkRed;
            cut.color = cross.color = second.color = ink;
            notch.color = Palette.Ink;
            var width = highlighted ? view.ScratchHighlightWidth : view.ScratchSize.y;
            cut.rectTransform.sizeDelta = new Vector2(view.ScratchSize.x, width);
            cross.rectTransform.sizeDelta = new Vector2(view.ScratchCrossSize.x,
                highlighted ? view.ScratchCrossSize.y * view.ScratchHighlightWidth / view.ScratchSize.y : view.ScratchCrossSize.y);
            second.rectTransform.sizeDelta = new Vector2(view.ScratchCrossSize.x, width);
            transform.localScale = new Vector3(Growth, 1, 1);
        }
    }
}
