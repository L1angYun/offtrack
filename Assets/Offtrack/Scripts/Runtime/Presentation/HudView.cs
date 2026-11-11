using System;
using System.Globalization;
using UnityEngine;
using UnityEngine.UI;

namespace Offtrack.Runtime.Presentation
{
    public sealed class HudView : FlowView
    {
        [SerializeField] private Text timer;
        [SerializeField] private RectTransform progress;
        [SerializeField] private FlatShape[] lives;
        [SerializeField] private GameObject idlePanel;
        [SerializeField] private GameObject resultPanel;
        [SerializeField] private Text resultTitle;
        [SerializeField] private Text resultBody;
        [SerializeField] private RectTransform playButton;
        [SerializeField] private RectTransform replayButton;
        [SerializeField] private AdShell shell;
        private int lastTenths = -1;
        private double lastDuration = -1;
        private int lastLives = -1;
        private Core.RunState? lastState;
        public string TimerText => timer != null ? timer.text : string.Empty;
        public RectTransform StartButton => shell != null
            ? shell.State == Core.AdShellState.FailDialog ? shell.Dialog.PrimaryButton
              : shell.State == Core.AdShellState.Idle ? playButton : null
            : flow != null && flow.State == Core.RunState.Ended ? replayButton : playButton;
        public int TimerWriteCount { get; private set; }

        public void SetShellManaged(AdShell value) => shell = value;

        public void ShowShellState(Core.AdShellState state)
        {
            idlePanel.SetActive(state == Core.AdShellState.Idle);
            resultPanel.SetActive(state == Core.AdShellState.Complete);
            replayButton.gameObject.SetActive(false);
            resultTitle.text = "SIDE A / COMPLETE";
            resultBody.text = "这一面，完整播完。";
        }

        public void Configure(GameFlow value, Text timerText, RectTransform progressFill, FlatShape[] lifeCells,
            GameObject idle, GameObject result, Text title, Text body, RectTransform play, RectTransform replay)
        {
            timer = timerText;
            progress = progressFill;
            lives = lifeCells;
            idlePanel = idle;
            resultPanel = result;
            resultTitle = title;
            resultBody = body;
            playButton = play;
            replayButton = replay;
            Bind(value);
        }

        public override void Refresh()
        {
            if (flow == null || flow.Simulation == null || timer == null) return;
            var sim = flow.Simulation;
            // 仅在 0.1 秒显示值改变时写入；事件可每步发出，文本不每帧重建。
            var tenths = (int)Math.Round(sim.Time * 10, MidpointRounding.AwayFromZero);
            if (lastTenths != tenths || lastDuration != flow.Config.Duration)
            {
                timer.text = (tenths / 10.0).ToString("00.0", CultureInfo.InvariantCulture) + " / " +
                    flow.Config.Duration.ToString("0.##", CultureInfo.InvariantCulture) + "s";
                lastTenths = tenths;
                lastDuration = flow.Config.Duration;
                TimerWriteCount++;
            }
            progress.sizeDelta = new Vector2(flow.Config.View.ProgressSize.x * (float)(sim.Time / flow.Config.Duration),
                flow.Config.View.ProgressSize.y);
            if (lastLives != sim.Lives)
            {
                for (var i = 0; i < lives.Length; i++) lives[i].color = i < sim.Lives ? Palette.Bone : Palette.DarkRed;
                lastLives = sim.Lives;
            }
            if (shell == null && lastState != sim.State)
            {
                idlePanel.SetActive(sim.State == Core.RunState.Idle);
                resultPanel.SetActive(sim.State == Core.RunState.Ended);
                if (sim.State == Core.RunState.Ended)
                {
                    resultTitle.text = sim.Result.Won ? "SIDE A / COMPLETE" : "THE NEEDLE NEEDS A BREAK";
                    resultBody.text = sim.Result.Won ? "这一面，完整播完。" : "音乐暂时停了。";
                }
                lastState = sim.State;
            }
        }
    }
}
