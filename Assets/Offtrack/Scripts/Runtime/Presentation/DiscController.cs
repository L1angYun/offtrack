using UnityEngine;

namespace Offtrack.Runtime.Presentation
{
    public sealed class DiscController : FlowView
    {
        [SerializeField] private RectTransform rotatingDisc;
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
        }
    }
}
