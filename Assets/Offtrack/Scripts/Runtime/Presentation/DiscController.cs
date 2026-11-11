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
            var config = flow.Config;
            var period = config.BeatsPerRevolution * (60.0 / config.Bpm);
            RotationDegrees = (float)(-flow.Simulation.Time * 360.0 / period);
            rotatingDisc.localRotation = Quaternion.Euler(0, 0, RotationDegrees);
        }
    }
}
