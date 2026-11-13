using System;
using System.Globalization;
using Offtrack.Core;
using UnityEngine;
using UnityEngine.UI;

namespace Offtrack.Runtime.Presentation
{
    /// <summary>
    /// The HUD as a turntable meter: a split timer, hollow lost durability cells, and a persistent
    /// needle row (lane, needle state, cooldown refill). GDD 5.3: a hit shows one static line
    /// naming the failure kind; nothing flashes or shakes. Text is rewritten only when it changes.
    /// </summary>
    public sealed class HudView : FlowView
    {
        public const string NeedleReady = "可跳针";
        public const string NeedleAirborne = "离针中";
        public const string NeedleSettling = "落针稳定";
        public const string NeedleDropping = "落针中";
        public const string NeedleStopped = "停机";
        public const string LateJumpNotice = "起跳过晚 · 耐久 −1";
        public const string EarlyLandingNotice = "落针过早 · 耐久 −1";

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
        [SerializeField] private Text timerRemaining;
        [SerializeField] private GameObject meter;
        [SerializeField] private FlatShape[] laneDots = Array.Empty<FlatShape>();
        [SerializeField] private Text needleStatus;
        [SerializeField] private RectTransform cooldownFill;
        [SerializeField] private float cooldownWidth;
        [SerializeField] private Text damageNotice;
        [SerializeField] private GameObject[] lifeHollows = Array.Empty<GameObject>();
        [SerializeField] private float damageNoticeSeconds = 2.5f;

        private int lastTenths = -1;
        private double lastDuration = -1;
        private int lastLives = -1;
        private RunState? lastState;
        private string lastNeedleStatus;
        private int lastLane = -1;
        private string damageKind;
        private double damageShownAt = double.NegativeInfinity;

        public string TimerText { get; private set; } = string.Empty;
        public string TimerValueText => timer != null ? timer.text : string.Empty;
        public string RemainingText => timerRemaining != null ? timerRemaining.text : string.Empty;
        public string NeedleStatusText => needleStatus != null ? needleStatus.text : string.Empty;
        public string DamageNoticeText => damageNotice != null && damageNotice.gameObject.activeSelf ? damageNotice.text : string.Empty;
        public float CooldownFraction { get; private set; } = 1;
        public bool MeterVisible => meter != null && meter.activeSelf;
        public RectTransform StartButton => shell != null
            ? shell.State == AdShellState.FailDialog ? shell.Dialog.PrimaryButton
              : shell.State == AdShellState.Idle ? playButton : null
            : flow != null && flow.State == RunState.Ended ? replayButton : playButton;
        public int TimerWriteCount { get; private set; }

        public void SetShellManaged(AdShell value) => shell = value;

        public void ShowShellState(AdShellState state)
        {
            idlePanel.SetActive(state == AdShellState.Idle);
            resultPanel.SetActive(state == AdShellState.Complete);
            replayButton.gameObject.SetActive(false);
            resultTitle.text = "SIDE A / COMPLETE";
            resultBody.text = "这一面，完整播完。";
            Refresh();
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

        public void ConfigureMeter(Text remaining, GameObject meterRoot, FlatShape[] lanes, Text status,
            RectTransform cooldown, float cooldownTrackWidth, Text notice, GameObject[] hollows, float noticeSeconds)
        {
            timerRemaining = remaining;
            meter = meterRoot;
            laneDots = lanes;
            needleStatus = status;
            cooldownFill = cooldown;
            cooldownWidth = cooldownTrackWidth;
            damageNotice = notice;
            lifeHollows = hollows;
            damageNoticeSeconds = noticeSeconds;
            lastTenths = -1; lastLives = -1; lastNeedleStatus = null; lastLane = -1;
            Refresh();
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            if (flow == null) return;
            flow.DamageTaken += HandleDamage;
            flow.RunStarted += HandleRunStarted;
        }

        protected override void OnDisable()
        {
            if (flow != null)
            {
                flow.DamageTaken -= HandleDamage;
                flow.RunStarted -= HandleRunStarted;
            }
            base.OnDisable();
        }

        // A new run (including the R shortcut) never inherits the previous run's notice.
        private void HandleRunStarted()
        {
            damageKind = null;
            damageShownAt = double.NegativeInfinity;
            Refresh();
        }

        private void HandleDamage(DamageEvent damage)
        {
            damageKind = damage.Kind;
            damageShownAt = flow.PresentationTime;
            if (damageNotice != null) damageNotice.text = NoticeFor(damage.Kind);
            Refresh();
        }

        /// <summary>GDD 5.3: the two failure kinds read differently.</summary>
        public static string NoticeFor(string kind) => kind == "landedTooEarly" ? EarlyLandingNotice : LateJumpNotice;

        public static string NeedleStateFor(GameFlow value)
        {
            var sim = value.Simulation;
            switch (value.Phase)
            {
                case MachinePhase.Starting: return NeedleDropping;
                case MachinePhase.Stopping:
                case MachinePhase.Stopped: return NeedleStopped;
                case MachinePhase.Running:
                    if (sim.Jump != null) return NeedleAirborne;
                    return sim.Time < sim.ReadyAt ? NeedleSettling : NeedleReady;
                default: return NeedleReady;
            }
        }

        public override void Refresh()
        {
            if (flow == null || flow.Simulation == null || timer == null) return;
            var sim = flow.Simulation;
            var duration = flow.Config.Duration;
            // Written only when the 0.1 s display value changes; events may fire every step.
            var tenths = (int)Math.Round(sim.Time * 10, MidpointRounding.AwayFromZero);
            if (lastTenths != tenths || lastDuration != duration)
            {
                var value = (tenths / 10.0).ToString("00.0", CultureInfo.InvariantCulture);
                TimerText = value + " / " + duration.ToString("0.##", CultureInfo.InvariantCulture) + "s";
                if (timerRemaining != null)
                {
                    timer.text = value;
                    // Same two-digit format as the live value, so the box never changes width.
                    timerRemaining.text = "还剩 " + Math.Max(0, duration - tenths / 10.0).ToString("00.0", CultureInfo.InvariantCulture) + " 秒";
                }
                else timer.text = TimerText;
                lastTenths = tenths;
                lastDuration = duration;
                TimerWriteCount++;
            }
            progress.sizeDelta = new Vector2(flow.Config.View.ProgressSize.x * (float)(sim.Time / duration),
                flow.Config.View.ProgressSize.y);
            if (lastLives != sim.Lives)
            {
                bool hollow = lifeHollows != null && lifeHollows.Length == lives.Length;
                for (var i = 0; i < lives.Length; i++)
                {
                    bool alive = i < sim.Lives;
                    // A lost cell keeps only its dark outline: emptied, not merely dimmed.
                    lives[i].color = alive ? Palette.Bone : hollow ? Palette.Ink : Palette.DarkRed;
                    if (hollow) lifeHollows[i].SetActive(!alive);
                }
                lastLives = sim.Lives;
            }
            RefreshMeter(sim);
            if (shell == null && lastState != sim.State)
            {
                idlePanel.SetActive(sim.State == RunState.Idle);
                resultPanel.SetActive(sim.State == RunState.Ended);
                if (sim.State == RunState.Ended)
                {
                    resultTitle.text = sim.Result.Won ? "SIDE A / COMPLETE" : "THE NEEDLE NEEDS A BREAK";
                    resultBody.text = sim.Result.Won ? "这一面，完整播完。" : "音乐暂时停了。";
                }
                lastState = sim.State;
            }
        }

        private void RefreshMeter(GameSimulation sim)
        {
            if (meter == null) return;
            // The idle call to action and the meter share one row: starting swaps the content.
            bool running = sim.State != RunState.Idle;
            if (meter.activeSelf != running) meter.SetActive(running);
            if (!running)
            {
                damageShownAt = double.NegativeInfinity;
                if (damageNotice != null && damageNotice.gameObject.activeSelf) damageNotice.gameObject.SetActive(false);
                return;
            }
            if (lastLane != sim.Lane)
            {
                for (int lane = 0; lane < laneDots.Length; lane++)
                    laneDots[lane].color = lane == sim.Lane ? Palette.Bone : Palette.DarkRed40;
                lastLane = sim.Lane;
            }
            var state = NeedleStateFor(flow);
            if (state != lastNeedleStatus)
            {
                needleStatus.text = state;
                needleStatus.color = state == NeedleReady ? Palette.Bone : Palette.Bone60;
                lastNeedleStatus = state;
            }
            CooldownFraction = CooldownFor(flow);
            if (cooldownFill != null)
                cooldownFill.sizeDelta = new Vector2(cooldownWidth * CooldownFraction, cooldownFill.sizeDelta.y);
            if (damageNotice != null)
            {
                bool show = flow.PresentationTime - damageShownAt < damageNoticeSeconds && damageKind != null
                    && flow.Phase != MachinePhase.Stopped;
                if (damageNotice.gameObject.activeSelf != show) damageNotice.gameObject.SetActive(show);
            }
        }

        /// <summary>Refill of the 0.57 s jump cycle (airborne plus settle); full when a jump is allowed.</summary>
        public static float CooldownFor(GameFlow value)
        {
            var sim = value.Simulation;
            switch (value.Phase)
            {
                case MachinePhase.Starting: return (float)value.PhaseProgress;
                case MachinePhase.Stopping: return 1 - (float)value.PhaseProgress;
                case MachinePhase.Stopped: return 0;
                case MachinePhase.Running:
                    double cycle = value.Config.JumpDuration + value.Config.SettleDuration;
                    if (cycle <= 0 || sim.Time >= sim.ReadyAt) return 1;
                    return Mathf.Clamp01((float)(1 - (sim.ReadyAt - sim.Time) / cycle));
                default: return 1;
            }
        }
    }
}
