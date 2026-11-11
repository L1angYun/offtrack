using UnityEngine;

namespace Offtrack.Runtime.Presentation
{
    public sealed class TonearmView : FlowView
    {
        [SerializeField] private RectTransform arm;
        [SerializeField] private RectTransform head;
        [SerializeField] private FlatShape tip;
        public float VisualLane { get; private set; }
        public float Lift { get; private set; }
        public Color TipColor => tip != null ? tip.color : Palette.Bone;

        public void Configure(GameFlow value, RectTransform armLine, RectTransform needleHead, FlatShape needleTip)
        {
            arm = armLine;
            head = needleHead;
            tip = needleTip;
            Bind(value);
        }

        public override void Refresh()
        {
            if (flow == null || flow.Simulation == null || arm == null) return;
            var sim = flow.Simulation;
            var view = flow.Config.View;
            VisualLane = sim.Lane;
            Lift = 0;
            if (sim.Jump != null)
            {
                var jump = sim.Jump;
                var p = Mathf.Clamp01((float)((sim.Time - jump.Start) / (jump.End - jump.Start)));
                var ease = p * p * (3 - 2 * p);
                VisualLane = Mathf.Lerp(jump.From, jump.To, ease);
                Lift = Mathf.Sin(p * Mathf.PI);
            }
            var lower = Mathf.FloorToInt(VisualLane);
            var upper = Mathf.Min(lower + 1, view.LaneRadiusRatios.Length - 1);
            var radius = view.DiscRadius * Mathf.Lerp(view.LaneRadiusRatios[lower],
                view.LaneRadiusRatios[upper], VisualLane - lower);
            var contact = view.DiscCenter + Vector2.up * radius;
            var tipPosition = contact + new Vector2(view.LiftX, view.LiftY) * Lift;
            // 本对象的 Pivot 即唱臂右端。针座在该坐标系内沿半径移动。
            var localTip = tipPosition - view.ArmPivot;
            head.anchoredPosition = localTip + view.HeadOffset;
            tip.rectTransform.anchoredPosition = localTip;
            var armEnd = localTip + view.HeadOffset;
            arm.sizeDelta = new Vector2(armEnd.magnitude, view.ArmWidth);
            arm.localRotation = Quaternion.Euler(0, 0, Mathf.Atan2(armEnd.y, armEnd.x) * Mathf.Rad2Deg);
            tip.color = sim.State == Core.RunState.Playing && sim.Time < sim.InvincibleUntil ? Palette.Red : Palette.Bone;
        }
    }
}
