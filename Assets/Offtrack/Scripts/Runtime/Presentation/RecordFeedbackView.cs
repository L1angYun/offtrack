using System;
using Offtrack.Core;
using UnityEngine;

namespace Offtrack.Runtime.Presentation
{
    public sealed class RecordFeedbackView : FlowView
    {
        [SerializeField] private FlatShape[] rings = Array.Empty<FlatShape>();
        [SerializeField] private FlatShape[] fragments = Array.Empty<FlatShape>();
        private double hitAt = double.NegativeInfinity;
        private double landAt = double.NegativeInfinity;
        private int hitLane = -1, landLane = -1, fragmentCount;

        public void Configure(GameFlow value, FlatShape[] laneRings, FlatShape[] debris)
        {
            rings = laneRings;
            fragments = debris;
            Bind(value);
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            if (flow == null) return;
            flow.DamageTaken += Hit;
            flow.Landed += Land;
            flow.RunStarted += ResetFeedback;
        }

        protected override void OnDisable()
        {
            if (flow != null)
            {
                flow.DamageTaken -= Hit;
                flow.Landed -= Land;
                flow.RunStarted -= ResetFeedback;
            }
            base.OnDisable();
        }

        private void Hit(DamageEvent damage)
        {
            hitLane = damage.Lane;
            hitAt = flow.PresentationTime;
            fragmentCount = 3 + damage.ScratchId % 2;
        }

        private void Land(LandingEvent landing)
        {
            landLane = landing.Lane;
            landAt = flow.PresentationTime;
        }

        private void ResetFeedback() => hitAt = landAt = double.NegativeInfinity;

        public override void Refresh()
        {
            if (flow == null || flow.Config == null) return;
            var view = flow.Config.View;
            double hitAge = flow.PresentationTime - hitAt;
            double landAge = flow.PresentationTime - landAt;
            for (int lane = 0; lane < rings.Length; lane++)
            {
                bool hit = lane == hitLane && hitAge < view.HitRingSeconds;
                bool landed = lane == landLane && landAge < view.LandingRingSeconds;
                rings[lane].gameObject.SetActive(hit || landed);
                if (hit) rings[lane].color = hitAge < view.HitRingSeconds / 3 ? Palette.Red :
                    hitAge < view.HitRingSeconds * 2 / 3 ? Palette.Red60 : Palette.Red40;
                else rings[lane].color = Palette.Bone60;
            }

            for (int index = 0; index < fragments.Length; index++)
            {
                bool active = index < fragmentCount && hitAge < view.DebrisSeconds;
                var piece = fragments[index];
                piece.gameObject.SetActive(active);
                if (!active) continue;
                float fraction = (float)(hitAge / view.DebrisSeconds);
                float direction = index % 2 == 0 ? 1 : -1;
                float size = view.DebrisSize * (1 - fraction);
                var point = view.DiscCenter + Vector2.up * (view.DiscRadius * view.LaneRadiusRatios[hitLane]);
                piece.rectTransform.anchoredPosition = point + new Vector2(direction * view.DebrisTravel.x * (index + 2) /
                    fragments.Length, view.DebrisTravel.y * (1 - index / 2f)) * fraction;
                piece.rectTransform.sizeDelta = new Vector2(size, size * .66f);
                piece.rectTransform.localRotation = Quaternion.Euler(0, 0, direction * 75 * (fraction + index));
            }
        }
    }
}
