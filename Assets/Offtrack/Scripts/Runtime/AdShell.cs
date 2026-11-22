using Offtrack.Core;
using Offtrack.Runtime.Presentation;
using UnityEngine;
using UnityEngine.UI;

namespace Offtrack.Runtime
{
    [DisallowMultipleComponent, DefaultExecutionOrder(-210)]
    public sealed class AdShell : MonoBehaviour
    {
        [SerializeField] private GameFlow flow;
        [SerializeField] private Camera inputCamera;
        [SerializeField] private RectTransform stage;
        [SerializeField] private RectTransform skipButton;
        [SerializeField] private FlatShape skipFill;
        [SerializeField] private Text skipLabel;
        [SerializeField] private GameObject boot;
        [SerializeField] private RectTransform bootProgress;
        [SerializeField] private DialogView dialog;
        [SerializeField] private GameObject modalDimmer;
        [SerializeField] private HudView hud;
        [SerializeField] private bool automaticTick = true;
        private double lastRealTime;
        private double lastRecoveryTime;
        private bool leaving;
        private int displayedCountdown = -1;
        private bool displayedReady;
        private double debounceRemaining;
        private bool keepDim;
        private double stayExitRemaining;
        private Canvas shellCanvas;
        private JourneyPerformance performance;

        internal static JourneyPerformance LastJourney { get; private set; }
        public AdShellMachine Machine { get; private set; }
        public AdShellState State => Machine?.State ?? AdShellState.Boot;
        public int Level => Machine?.Level ?? 1;
        public int Fragments => Machine?.Fragments ?? 0;
        public GameFlow Flow => flow;
        public RectTransform Stage => stage;
        public RectTransform SkipButton => skipButton;
        public DialogView Dialog => dialog;
        public bool AutomaticTick
        {
            get => automaticTick;
            set { automaticTick = value; lastRealTime = lastRecoveryTime = Time.realtimeSinceStartupAsDouble; }
        }

        public void Configure(GameFlow value, Camera camera, RectTransform stageArea, RectTransform skip,
            FlatShape skipBackground, Text skipText, GameObject bootPage,
            RectTransform loadingFill, DialogView dialogView, HudView hudView, GameObject dimmer = null)
        {
            Detach();
            flow = value; inputCamera = camera; stage = stageArea; skipButton = skip;
            skipFill = skipBackground; skipLabel = skipText;
            boot = bootPage; bootProgress = loadingFill; dialog = dialogView; hud = hudView;
            modalDimmer = dimmer;
            hud.SetShellManaged(this);
            Initialize();
        }

        private void Awake()
        {
            if (flow != null) Initialize();
        }

        private void OnEnable()
        {
            Attach();
            lastRealTime = lastRecoveryTime = Time.realtimeSinceStartupAsDouble;
            if (Machine != null) RefreshState(State);
        }

        private void OnDisable()
        {
            Detach();
            SuspendFlow();
        }

        private void SuspendFlow()
        {
            if (flow == null) return;
            flow.SetShellPaused(true);
            flow.SetNeedleInputEnabled(false);
            flow.SetRunStartEnabled(false);
        }

        private void Initialize()
        {
            Detach();
            shellCanvas = dialog.GetComponentInParent<Canvas>(true);
            Machine = new AdShellMachine(flow.Config.Shell);
            if (Application.isPlaying) LastJourney = performance = new JourneyPerformance();
            leaving = false; displayedCountdown = -1;
            lastRealTime = lastRecoveryTime = Time.realtimeSinceStartupAsDouble;
            Attach();
            if (isActiveAndEnabled) RefreshState(State); else SuspendFlow();
        }

        private void Attach()
        {
            if (Machine == null || flow == null || !isActiveAndEnabled) return;
            Detach();
            Machine.StateChanged += RefreshState;
            Machine.PauseChanged += RefreshFlowGates;
            flow.RunStarted += HandleRunStarted;
            flow.RunStopped += HandleRunStopped;
            flow.DamageTaken += HandleDamage;
        }

        private void Detach()
        {
            if (Machine != null) Machine.StateChanged -= RefreshState;
            if (Machine != null) Machine.PauseChanged -= RefreshFlowGates;
            if (flow == null) return;
            flow.RunStarted -= HandleRunStarted;
            flow.RunStopped -= HandleRunStopped;
            flow.DamageTaken -= HandleDamage;
        }

        private void Update()
        {
            double now = Time.realtimeSinceStartupAsDouble;
            double elapsed = now - lastRealTime;
            double recoveryElapsed = System.Math.Min(elapsed, System.Math.Max(0, now - lastRecoveryTime));
            lastRealTime = lastRecoveryTime = now;
            if (automaticTick) AdvanceClocks(elapsed, recoveryElapsed);
        }

        // Unity may suspend Update entirely in the background. Reset only the recovery sample on
        // both transition edges so the first foreground frame cannot consume the suspended interval.
        // The independent ad sample keeps its elapsed time and the existing skip-countdown semantics.
        private void OnApplicationFocus(bool focused) => lastRecoveryTime = Time.realtimeSinceStartupAsDouble;
        private void OnApplicationPause(bool paused) => lastRecoveryTime = Time.realtimeSinceStartupAsDouble;

        public void Advance(double realSeconds) => AdvanceClocks(realSeconds, realSeconds);

        private void AdvanceClocks(double realSeconds, double recoverySeconds)
        {
            if (Machine == null || !isActiveAndEnabled) return;
            dialog.Advance((float)realSeconds);
            debounceRemaining = System.Math.Max(0, debounceRemaining - realSeconds);
            if (stayExitRemaining > 0)
            {
                stayExitRemaining -= realSeconds;
                if (stayExitRemaining <= 0)
                {
                    Machine.Advance(realSeconds, 0);
                    Machine.Stay(flow.Config.Shell.DialogExitDuration);
                    RefreshProgress();
                    return;
                }
            }
            Machine.Advance(realSeconds, flow.IsApplicationPaused ? 0 : recoverySeconds);
            RefreshProgress();
        }

        public bool AcceptLottery() => isActiveAndEnabled && Machine != null && Machine.AcceptLottery();
        public bool AcceptFragment()
        {
            if (!isActiveAndEnabled || State != AdShellState.Fragment) return false;
            // Keep the fragment dialog available until the next chart has loaded.
            if (Level < 3)
            {
                if (flow.Config.Levels.Length >= Level + 1) flow.SelectLevel(Level + 1);
                else flow.ResetToIdle();
            }
            if (!Machine.AcceptFragment()) return false;
            if (State == AdShellState.LevelTransition) Machine.OpenNextGuide();
            return true;
        }
        private bool SkipAvailable => Machine.CanSkip && !SceneTransition.BlocksInput && !flow.IsTransitioning;
        public bool RequestSkip()
        {
            if (!isActiveAndEnabled || Machine == null || !SkipAvailable || debounceRemaining > 0) return false;
            return WithSteadyDim(Machine.RequestSkip);
        }
        public bool Stay()
        {
            if (!isActiveAndEnabled || Machine == null || !Machine.Stay()) return false;
            lastRecoveryTime = Time.realtimeSinceStartupAsDouble;
            return true;
        }
        public bool GiveUp() => isActiveAndEnabled && Machine != null && Machine.GiveUp();
        public bool StartPlaying()
        {
            // The needle drops before judging starts; a run cannot start mid-transition.
            if (!isActiveAndEnabled || Machine == null || flow.IsTransitioning || !Machine.StartPlaying()) return false;
            flow.StartShellRun(); return true;
        }
        public bool Retry()
        {
            if (!isActiveAndEnabled || Machine == null || State != AdShellState.FailDialog) return false;
            return FadeToStart(true);
        }
        public bool Restart()
        {
            if (!isActiveAndEnabled || Machine == null || flow.IsTransitioning || !Machine.Restart()) return false;
            flow.StartShellRun(); return true;
        }

        private void HandleRunStarted()
        {
            performance?.BeginAttempt();
            Machine.StartPlaying();
        }
        private void HandleRunStopped(RunResult result)
        {
            if (Machine.NotifyRunEnded(result.Won) && result.Won)
                performance?.ClearLevel();
        }
        private void HandleDamage(DamageEvent _) => performance?.RecordHit();

        private void RefreshState(AdShellState state)
        {
            RefreshFlowGates();
            boot.SetActive(state == AdShellState.Boot);
            dialog.Show(state, Level, modalDimmer, keepDim, Fragments); hud.ShowShellState(state); RefreshProgress();
            if (state != AdShellState.Leaving) return;
            performance?.FinalizeResults(Machine.SkippedGame, Machine.UnplayedLevelsOnAbandon);
            if (Application.isPlaying && !leaving) LeaveForEnding();
        }

        private void RefreshFlowGates()
        {
            flow.SetShellPaused(Machine.IsGamePaused || State == AdShellState.Leaving);
            flow.SetNeedleInputEnabled(Machine.AllowsNeedleInput);
            flow.SetRunStartEnabled(Machine.AllowsRestart);
        }

        private void RefreshProgress()
        {
            bootProgress.sizeDelta = new Vector2(flow.Config.ShellView.BootProgressSize.x * (float)Machine.BootProgress,
                flow.Config.ShellView.BootProgressSize.y);
            int remaining = Machine.SkipSecondsRemaining;
            bool ready = SkipAvailable;
            if (remaining == displayedCountdown && ready == displayedReady) return;
            skipLabel.text = remaining > 0 ? "跳过 " + remaining : "跳过";
            skipFill.color = ready ? Palette.Red : Palette.DarkRed15;
            skipLabel.color = ready ? Palette.Bone : Palette.Bone40;
            displayedCountdown = remaining; displayedReady = ready;
        }

        /// <summary>Consume shell clicks before any keyboard or stage input in the same frame.</summary>
        public bool HandlePointer(Vector2 screenPoint)
        {
            if (SceneTransition.BlocksInput || !isActiveAndEnabled || flow.IsApplicationPaused) return true;
            if (State == AdShellState.Boot || State == AdShellState.Leaving) return true;
            if (Contains(skipButton, screenPoint)) { RequestSkip(); return true; }
            if (Machine.IsModalOpen)
            {
                if (Contains(dialog.PrimaryButton, screenPoint))
                {
                    ActivatePrimary();
                }
                else if (Contains(dialog.SecondaryButton, screenPoint))
                {
                    if (TryPress(dialog.SecondaryButton))
                    {
                        if (State == AdShellState.ConfirmSkip)
                        {
                            if (Machine.GiveUpContinues) GiveUpToNextLevel();
                            else LeaveForEnding(GiveUp);
                        }
                        else if (State == AdShellState.FailDialog) WithSteadyDim(Machine.RequestAbandonAfterFailure);
                    }
                }
                return true;
            }
            return false;
        }

        public void ActivatePrimary()
        {
            if (!TryPress(dialog.PrimaryButton)) return;
            var rules = flow.Config.Shell;
            switch (State)
            {
                case AdShellState.Lottery:
                case AdShellState.Fragment when Level < 3:
                    bool lottery = State == AdShellState.Lottery;
                    SceneTransition.FadeLocally(shellCanvas, rules.DialogExitDuration, rules.DialogEntranceDuration,
                        Palette.DialogDim.a / 255.0, () =>
                        {
                            if (this == null) return;
                            if (lottery) AcceptLottery(); else AcceptFragment();
                            dialog.UseTransitionDim(true);
                        }, () => { if (this != null) dialog.UseTransitionDim(false); });
                    break;
                case AdShellState.Fragment:
                    LeaveForEnding(AcceptFragment);
                    break;
                case AdShellState.Guide:
                    FadeToStart(false);
                    break;
                case AdShellState.FailDialog:
                    Retry();
                    break;
                case AdShellState.ConfirmSkip:
                    if (Machine.PreviousState == AdShellState.Playing)
                    {
                        stayExitRemaining = rules.DialogExitDuration;
                        dialog.BeginExit();
                    }
                    else WithSteadyDim(Stay);
                    break;
            }
        }

        // Giving up costs only this level: the next level's chart loads behind the same local
        // fade the fragment dialog uses, and its guide opens.
        private void GiveUpToNextLevel()
        {
            var rules = flow.Config.Shell;
            SceneTransition.FadeLocally(shellCanvas, rules.DialogExitDuration, rules.DialogEntranceDuration,
                Palette.DialogDim.a / 255.0, () =>
                {
                    if (this == null) return;
                    if (flow.Config.Levels.Length >= Level + 1) flow.SelectLevel(Level + 1);
                    else flow.ResetToIdle();
                    GiveUp();
                    dialog.UseTransitionDim(true);
                }, () => { if (this != null) dialog.UseTransitionDim(false); });
        }

        private bool TryPress(RectTransform button)
        {
            if (!Machine.IsModalOpen || SceneTransition.BlocksInput || !dialog.CanPress || debounceRemaining > 0) return false;
            debounceRemaining = flow.Config.Shell.DialogDebounceDuration;
            dialog.Press(button);
            return true;
        }

        private bool FadeToStart(bool retry)
        {
            var rules = flow.Config.Shell;
            double duration = retry ? rules.RetryBlackDuration : rules.DialogExitDuration;
            return SceneTransition.FadeLocally(shellCanvas, duration, duration, 0, () =>
            {
                if (this == null) return;
                if (retry)
                {
                    Machine.Retry();
                    flow.ResetToIdle();
                }
                dialog.Show(AdShellState.LevelTransition, Level, modalDimmer);
                hud.ShowShellState(AdShellState.LevelTransition);
            }, () =>
            {
                if (this == null || !isActiveAndEnabled) return;
                StartPlaying();
            });
        }

        private bool WithSteadyDim(System.Func<bool> action)
        {
            keepDim = Machine.IsModalOpen;
            try { return action(); }
            finally { keepDim = false; }
        }

        private void LeaveForEnding(System.Func<bool> action = null)
        {
            leaving = SceneTransition.GoTo(OfftrackScenes.CutsceneC, flow.Config.Cutscenes.Transition,
                () => { if (this != null) action?.Invoke(); });
        }

        private bool Contains(RectTransform rect, Vector2 point) => rect != null && rect.gameObject.activeInHierarchy &&
            RectTransformUtility.RectangleContainsScreenPoint(rect, point, inputCamera);
    }
}
