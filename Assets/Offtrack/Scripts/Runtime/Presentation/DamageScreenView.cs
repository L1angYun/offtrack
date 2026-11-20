using Offtrack.Core;
using UnityEngine;

namespace Offtrack.Runtime.Presentation
{
    [DisallowMultipleComponent]
    public sealed class DamageScreenView : MonoBehaviour
    {
        private GameFlow flow;
        private GameSimulation simulation;
        private DamageVignette vignette;
        private double hitAt = double.NegativeInfinity;
        public DamageVignette Vignette => vignette;
        public float Strength => vignette == null ? 0 : vignette.Strength;

        public void Configure(GameFlow value, Canvas canvas)
        {
            Detach();
            flow = value;
            simulation = flow.Simulation;
            if (vignette == null)
            {
                var node = new GameObject("DamageVignette", typeof(RectTransform), typeof(CanvasGroup), typeof(DamageVignette));
                var rect = (RectTransform)node.transform;
                rect.SetParent(canvas.rootCanvas.transform, false);
                rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
                rect.offsetMin = rect.offsetMax = Vector2.zero;
                rect.SetAsLastSibling();
                var group = node.GetComponent<CanvasGroup>();
                group.interactable = group.blocksRaycasts = false;
                vignette = node.GetComponent<DamageVignette>();
                vignette.color = Palette.Red;
                vignette.raycastTarget = false;
            }
            hitAt = double.NegativeInfinity;
            if (isActiveAndEnabled) Attach();
            Refresh();
        }

        private void OnEnable() { Attach(); Refresh(); }
        private void OnDisable() { Detach(); Clear(); }
        private void OnDestroy() { if (vignette != null) Destroy(vignette.gameObject); }
        private void Update() => Refresh();

        private void Attach()
        {
            if (flow == null) return;
            Detach();
            flow.DamageTaken += Hit;
            flow.RunStarted += Clear;
            flow.PhaseChanged += PhaseChanged;
            flow.Changed += Refresh;
        }

        private void Detach()
        {
            if (flow == null) return;
            flow.DamageTaken -= Hit;
            flow.RunStarted -= Clear;
            flow.PhaseChanged -= PhaseChanged;
            flow.Changed -= Refresh;
        }

        private void Hit(DamageEvent damage) { hitAt = flow.PresentationTime; Refresh(); }
        private void PhaseChanged(MachinePhase phase) { if (phase == MachinePhase.Parked) Clear(); }
        private void Clear() { hitAt = double.NegativeInfinity; Refresh(); }

        private void Refresh()
        {
            if (flow == null || vignette == null || flow.Config == null) return;
            if (simulation != flow.Simulation) { simulation = flow.Simulation; hitAt = double.NegativeInfinity; }
            var settings = flow.Config.DamageFeedback;
            float strength = flow.IsPaused ? 0 : settings.Strength(flow.PresentationTime - hitAt);
            vignette.SetFeedback(strength, settings);
            vignette.enabled = strength > 0;
        }
    }
}
