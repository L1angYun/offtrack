using UnityEngine;

namespace Offtrack.Runtime.Presentation
{
    public sealed class DiscController : FlowView
    {
        [SerializeField] private RectTransform rotatingDisc;
        private Transform reversalStamp, reversalArrow;
        public float RotationDegrees { get; private set; }

        public void Configure(GameFlow value, RectTransform disc)
        {
            rotatingDisc = disc;
            Bind(value);
        }

        public override void Refresh()
        {
            if (flow == null || flow.Simulation == null || rotatingDisc == null) return;
            // Running follows the judge clock exactly; the drop and the stop add uniform spin-up/down.
            RotationDegrees = (float)flow.DiscRotationDegrees;
            rotatingDisc.localRotation = Quaternion.Euler(0, 0, RotationDegrees);
            if (reversalStamp == null)
            {
                reversalStamp = rotatingDisc.Find("ReversalStamp");
                if (reversalStamp == null) return;
                reversalArrow = reversalStamp.Find("Arrow");
            }
            bool enabled = flow.Record?.Reversals.Count > 0;
            reversalStamp.gameObject.SetActive(enabled);
            if (enabled)
            {
                int direction = flow.Record.LabelDirection(flow.Simulation.Time / flow.Record.BeatSeconds, flow.Level.ArrowLead);
                reversalArrow.localRotation = Quaternion.Euler(0, 0, direction > 0 ? 0 : 180);
            }
        }
    }
}
