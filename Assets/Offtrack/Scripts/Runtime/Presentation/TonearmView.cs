using Offtrack.Core;
using UnityEngine;

namespace Offtrack.Runtime.Presentation
{
    /// <summary>
    /// A flat mechanical tonearm: two-tone tube, counterweight, bearing, angled headshell with a
    /// finger lift, and a stylus that reaches the contact point. Every pose is computed from the
    /// flow's clocks (judge time, machine phase, presentation time), so pauses freeze it exactly.
    /// The contact geometry and the judge stay unchanged: all three contact points sit at 12 o'clock.
    /// </summary>
    public sealed class TonearmView : FlowView
    {
        [SerializeField] private RectTransform arm;
        [SerializeField] private RectTransform head;
        [SerializeField] private FlatShape tip;
        [SerializeField] private RectTransform inlay;
        [SerializeField] private RectTransform counterweight;
        [SerializeField] private RectTransform stylus;
        [SerializeField] private FlatShape shell;

        private double nudgeStart = double.NegativeInfinity;
        private int nudgeDirection;
        // The last pose presented while running: the stop starts exactly from it (a win can end
        // mid-jump, and a fatal hit leaves the red tip in the groove).
        private float endLane = -1;
        private float endLift;

        public float VisualLane { get; private set; }
        /// <summary>0 when the stylus rests in the groove, 1 when fully raised.</summary>
        public float Lift { get; private set; }
        /// <summary>0 parked on the rest, 1 over the record.</summary>
        public float Swing { get; private set; }
        public bool TipVisible => tip != null && tip.gameObject.activeSelf;
        public Color TipColor => tip != null ? tip.color : Palette.Bone;
        public Color ShellColor => shell != null ? shell.color : Palette.Bone;
        public float TipRadius => tip != null ? tip.rectTransform.sizeDelta.x / 2 : 0;
        public float HeadScale => head != null ? head.localScale.x : 1;
        public Vector2 TipPosition => tip != null ? tip.rectTransform.anchoredPosition : Vector2.zero;
        public Vector2 NudgeOffset { get; private set; }
        public int RejectedResponses { get; private set; }

        public void Configure(GameFlow value, RectTransform armLine, RectTransform needleHead, FlatShape needleTip)
            => Configure(value, armLine, needleHead, needleTip, null, null, null, null);

        public void Configure(GameFlow value, RectTransform armBody, RectTransform headshell, FlatShape needleTip,
            RectTransform armInlay, RectTransform weight, RectTransform stylusLine, FlatShape shellFill)
        {
            arm = armBody; head = headshell; tip = needleTip;
            inlay = armInlay; counterweight = weight; stylus = stylusLine; shell = shellFill;
            Bind(value);
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            if (flow != null) flow.MoveAttempted += HandleMove;
        }

        protected override void OnDisable()
        {
            if (flow != null) flow.MoveAttempted -= HandleMove;
            base.OnDisable();
        }

        private void HandleMove(int direction, MoveResult result)
        {
            if (result.Result == "jump") return;
            // Every refused press is acknowledged by the machine (cooldown, airborne, edge, drop).
            nudgeStart = flow.PresentationTime;
            nudgeDirection = direction;
            RejectedResponses++;
            Refresh();
        }

        public override void Refresh()
        {
            if (flow == null || flow.Simulation == null || arm == null) return;
            var sim = flow.Simulation;
            var view = flow.Config.View;
            VisualLane = sim.Lane;
            Lift = 0;
            Swing = 1;
            float progress = (float)flow.PhaseProgress;
            switch (flow.Phase)
            {
                case MachinePhase.Parked:
                case MachinePhase.Stopped:
                    Swing = 0; Lift = 1;
                    break;
                case MachinePhase.Starting:
                    // Swing in raised, then lower the stylus into the groove.
                    Swing = Smooth(progress / Mathf.Max(.01f, view.DropSwingShare));
                    Lift = progress < view.DropSwingShare ? 1
                        : 1 - Smooth((progress - view.DropSwingShare) / Mathf.Max(.01f, 1 - view.DropSwingShare));
                    break;
                case MachinePhase.Stopping:
                    // Hold the last pose for a beat, lift, then swing back to the rest. The
                    // motion is identical for a win and a loss; only the starting pose differs.
                    if (endLane >= 0) VisualLane = endLane;
                    float hold = view.StopHoldShare, lift = Mathf.Max(.01f, view.StopLiftShare);
                    Lift = progress < hold ? endLift : Mathf.Lerp(endLift, 1, Smooth((progress - hold) / lift));
                    Swing = progress < hold + lift ? 1
                        : 1 - Smooth((progress - hold - lift) / Mathf.Max(.01f, 1 - hold - lift));
                    break;
                default:
                    if (sim.Jump != null)
                    {
                        var jump = sim.Jump;
                        var p = Mathf.Clamp01((float)((sim.Time - jump.Start) / (jump.End - jump.Start)));
                        VisualLane = Mathf.Lerp(jump.From, jump.To, Smooth(p));
                        Lift = Mathf.Sin(p * Mathf.PI);
                    }
                    endLane = VisualLane;
                    endLift = Lift;
                    break;
            }

            var lower = Mathf.Clamp(Mathf.FloorToInt(VisualLane), 0, view.LaneRadiusRatios.Length - 1);
            var upper = Mathf.Min(lower + 1, view.LaneRadiusRatios.Length - 1);
            var radius = view.DiscRadius * Mathf.Lerp(view.LaneRadiusRatios[lower],
                view.LaneRadiusRatios[upper], VisualLane - lower);
            var contact = view.DiscCenter + Vector2.up * radius - view.ArmPivot;
            var raised = contact + new Vector2(view.LiftX, view.LiftY) * Lift;
            var localTip = Polar(view.ArmRestTip, raised, Swing);
            if (flow.Phase == MachinePhase.Parked) endLane = -1;

            // A refused press pushes the head 3-4 px toward the requested lane and back (~90 ms).
            // The stylus tip stays in the groove; the stylus line stretches to the moved head.
            NudgeOffset = Vector2.zero;
            double since = flow.PresentationTime - nudgeStart;
            if (since >= 0 && since < view.NudgeDuration)
                NudgeOffset = Vector2.up * (-nudgeDirection) * view.NudgeDistance *
                    Mathf.Sin((float)(since / view.NudgeDuration) * Mathf.PI);

            var armEnd = localTip + view.HeadOffset + NudgeOffset;
            var angle = Mathf.Atan2(armEnd.y, armEnd.x) * Mathf.Rad2Deg;
            var direction = armEnd.normalized;
            arm.sizeDelta = new Vector2(armEnd.magnitude, view.ArmWidth);
            arm.localRotation = Quaternion.Euler(0, 0, angle);
            if (inlay != null)
            {
                inlay.sizeDelta = new Vector2(Mathf.Max(0, armEnd.magnitude - view.InlayInset), view.ArmInlayWidth);
                inlay.localRotation = arm.localRotation;
            }
            if (counterweight != null)
            {
                counterweight.anchoredPosition = -direction * view.CounterweightOffset;
                counterweight.localRotation = arm.localRotation;
            }

            head.anchoredPosition = armEnd;
            head.localRotation = Quaternion.Euler(0, 0, angle + view.HeadshellAngle);
            head.localScale = Vector3.one * HeadScaleFor(sim, view);
            if (stylus != null)
            {
                var toHead = armEnd - localTip;
                stylus.anchoredPosition = localTip;
                stylus.sizeDelta = new Vector2(toHead.magnitude, view.StylusWidth);
                stylus.localRotation = Quaternion.Euler(0, 0, Mathf.Atan2(toHead.y, toHead.x) * Mathf.Rad2Deg);
            }

            // GDD 5.3: a hit only recolours (and here enlarges) the needle tip; a fatal hit stays red.
            bool hurt = sim.State != RunState.Idle && sim.Time < sim.InvincibleUntil;
            tip.rectTransform.anchoredPosition = localTip;
            tip.rectTransform.sizeDelta = Vector2.one * (hurt ? view.TipHitRadius : view.TipRadius) * 2;
            tip.color = hurt ? Palette.Red : Palette.Bone;
            // The contact point exists only while the stylus is in the groove.
            bool grounded = Swing >= 1 && Lift <= .02f;
            if (tip.gameObject.activeSelf != grounded) tip.gameObject.SetActive(grounded);
            if (shell != null)
            {
                bool settling = flow.Phase == MachinePhase.Running && sim.Jump == null && sim.Time < sim.ReadyAt;
                shell.color = settling ? Palette.Bone60 : Palette.Bone;
            }
        }

        private float HeadScaleFor(GameSimulation sim, ViewSettings view)
        {
            // Raised over the record the headshell reads slightly closer; parked it is at rest.
            if (Lift > 0) return 1 + view.AirborneScale * Lift * Swing;
            if (flow.Phase != MachinePhase.Running) return 1;
            double landed = sim.Time - sim.LastLanding;
            if (landed < 0 || landed >= view.PressDuration) return 1;
            // Landing press: a short dip that makes the end of the immunity window visible.
            return 1 - view.PressDepth * Mathf.Sin((float)(landed / view.PressDuration) * Mathf.PI);
        }

        private static float Smooth(float value)
        {
            value = Mathf.Clamp01(value);
            return value * value * (3 - 2 * value);
        }

        // Rotate around the bearing instead of cutting across the record.
        private static Vector2 Polar(Vector2 from, Vector2 to, float t)
        {
            if (t >= 1) return to;
            if (t <= 0) return from;
            float angle = Mathf.LerpAngle(Mathf.Atan2(from.y, from.x) * Mathf.Rad2Deg, Mathf.Atan2(to.y, to.x) * Mathf.Rad2Deg, t);
            float length = Mathf.Lerp(from.magnitude, to.magnitude, t);
            float radians = angle * Mathf.Deg2Rad;
            return new Vector2(Mathf.Cos(radians), Mathf.Sin(radians)) * length;
        }
    }
}
