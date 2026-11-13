using Offtrack.Core;
using UnityEngine;
using UnityEngine.UI;

namespace Offtrack.Runtime.Presentation
{
    // Mechanical status follows the simulation clock exactly: no independent timer or easing,
    // so a modal pause, hit, or a stopped run cannot introduce a separate visual animation.
    public sealed class TurntableView : FlowView
    {
        [SerializeField] private Text status;
        [SerializeField] private Text sidePrint;
        [SerializeField] private FlatShape[] beatLights;
        private double displayedBpm = double.NaN;
        private MachinePhase displayedPhase = (MachinePhase)(-1);
        private int displayedLane = -1;
        public int ActiveBeat { get; private set; } = -1;

        public static string StatusFor(MachinePhase phase, int lane)
        {
            switch (phase)
            {
                case MachinePhase.Starting: return "落针中";
                case MachinePhase.Running: return "● 正在播放 · 第 " + (lane + 1) + " 轨";
                case MachinePhase.Stopping: return "停机中";
                case MachinePhase.Stopped: return "播放停止";
                default: return "等待落针";
            }
        }

        public void Configure(GameFlow value, Text statusLabel, Text sideLabel, FlatShape[] lights)
        {
            status = statusLabel;
            sidePrint = sideLabel;
            beatLights = lights;
            displayedBpm = double.NaN;
            displayedLane = -1;
            displayedPhase = (MachinePhase)(-1);
            Bind(value);
        }

        public override void Refresh()
        {
            if (flow == null || flow.Simulation == null || status == null || sidePrint == null || beatLights == null) return;
            var simulation = flow.Simulation;
            if (displayedBpm != flow.Config.Bpm)
            {
                sidePrint.text = "SIDE A / " + flow.Config.Bpm.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + " BPM";
                displayedBpm = flow.Config.Bpm;
            }
            var phase = flow.Phase;
            if (displayedPhase != phase || displayedLane != simulation.Lane)
            {
                status.text = StatusFor(phase, simulation.Lane);
                displayedPhase = phase;
                displayedLane = simulation.Lane;
            }
            ActiveBeat = simulation.State == RunState.Idle || phase == MachinePhase.Starting || beatLights.Length == 0 ? -1
                : (int)System.Math.Floor(simulation.Time * flow.Config.Bpm / 60.0) % beatLights.Length;
            for (int i = 0; i < beatLights.Length; i++)
                beatLights[i].color = i == ActiveBeat ? Palette.Bone : Palette.DarkRed40;
        }
    }
}
