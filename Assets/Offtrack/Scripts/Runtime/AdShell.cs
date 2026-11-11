using Offtrack.Core;
using Offtrack.Runtime.Presentation;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Offtrack.Runtime
{
    [DisallowMultipleComponent, DefaultExecutionOrder(-150)]
    public sealed class AdShell : MonoBehaviour
    {
        [SerializeField] private GameFlow flow;
        [SerializeField] private Camera inputCamera;
        [SerializeField] private RectTransform stage;
        [SerializeField] private RectTransform skipButton;
        [SerializeField] private FlatShape skipFill;
        [SerializeField] private Text skipLabel;
        [SerializeField] private RectTransform playButton;
        [SerializeField] private GameObject boot;
        [SerializeField] private RectTransform bootProgress;
        [SerializeField] private DialogView dialog;
        [SerializeField] private HudView hud;
        [SerializeField] private bool automaticTick = true;
        private double lastRealTime;
        private bool leaving;
        private int displayedCountdown = -1;
        private bool displayedReady;

        public AdShellMachine Machine { get; private set; }
        public AdShellState State => Machine?.State ?? AdShellState.Boot;
        public GameFlow Flow => flow;
        public RectTransform Stage => stage;
        public RectTransform SkipButton => skipButton;
        public DialogView Dialog => dialog;
        public bool AutomaticTick
        {
            get => automaticTick;
            set { automaticTick = value; lastRealTime = Time.realtimeSinceStartupAsDouble; }
        }

        public void Configure(GameFlow value, Camera camera, RectTransform stageArea, RectTransform skip,
            FlatShape skipBackground, Text skipText, RectTransform play, GameObject bootPage,
            RectTransform loadingFill, DialogView dialogView, HudView hudView)
        {
            Detach();
            flow = value; inputCamera = camera; stage = stageArea; skipButton = skip;
            skipFill = skipBackground; skipLabel = skipText; playButton = play;
            boot = bootPage; bootProgress = loadingFill; dialog = dialogView; hud = hudView;
            hud.SetShellManaged(this);
            Initialize();
        }

        private void Awake()
        {
            if (flow != null) Initialize();
#if UNITY_STANDALONE && !UNITY_EDITOR
            if (flow != null && flow.Config.Cutscenes.ResizeStandaloneWindow && !Screen.fullScreen)
                Screen.SetResolution(flow.Config.Cutscenes.MainWindowWidth,
                    flow.Config.Cutscenes.MainWindowHeight, FullScreenMode.Windowed);
#endif
        }

        private void OnEnable()
        {
            Attach();
            lastRealTime = Time.realtimeSinceStartupAsDouble;
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
            Machine = new AdShellMachine(flow.Config.Shell);
            leaving = false; displayedCountdown = -1;
            lastRealTime = Time.realtimeSinceStartupAsDouble;
            Attach();
            if (isActiveAndEnabled) RefreshState(State); else SuspendFlow();
        }

        private void Attach()
        {
            if (Machine == null || flow == null || !isActiveAndEnabled) return;
            Detach();
            Machine.StateChanged += RefreshState;
            flow.RunStarted += HandleRunStarted;
            flow.RunEnded += HandleRunEnded;
        }

        private void Detach()
        {
            if (Machine != null) Machine.StateChanged -= RefreshState;
            if (flow == null) return;
            flow.RunStarted -= HandleRunStarted;
            flow.RunEnded -= HandleRunEnded;
        }

        private void Update()
        {
            double now = Time.realtimeSinceStartupAsDouble;
            double elapsed = now - lastRealTime;
            lastRealTime = now;
            if (automaticTick) Advance(elapsed);
        }

        public void Advance(double realSeconds)
        {
            if (Machine == null || !isActiveAndEnabled) return;
            Machine.Advance(realSeconds); RefreshProgress();
        }

        public bool AcceptLottery() => isActiveAndEnabled && Machine != null && Machine.AcceptLottery();
        public bool RequestSkip() => isActiveAndEnabled && Machine != null && Machine.RequestSkip();
        public bool Stay() => isActiveAndEnabled && Machine != null && Machine.Stay();
        public bool GiveUp() => isActiveAndEnabled && Machine != null && Machine.GiveUp();
        public bool StartPlaying()
        {
            if (!isActiveAndEnabled || Machine == null || !Machine.StartPlaying()) return false;
            flow.StartRun(); return true;
        }
        public bool Retry()
        {
            if (!isActiveAndEnabled || Machine == null || !Machine.Retry()) return false;
            flow.ResetToIdle(); return true;
        }
        public bool Restart()
        {
            if (!isActiveAndEnabled || Machine == null || !Machine.Restart()) return false;
            flow.StartRun(); return true;
        }

        private void HandleRunStarted()
        {
            if (State == AdShellState.Complete) Machine.Restart();
            else Machine.StartPlaying();
        }
        private void HandleRunEnded(RunResult result) => Machine.NotifyRunEnded(result.Won);

        private void RefreshState(AdShellState state)
        {
            flow.SetShellPaused(Machine.IsGamePaused || state == AdShellState.Leaving);
            flow.SetNeedleInputEnabled(Machine.AllowsNeedleInput);
            flow.SetRunStartEnabled(Machine.AllowsRestart);
            boot.SetActive(state == AdShellState.Boot);
            dialog.Show(state); hud.ShowShellState(state); RefreshProgress();
            if (state == AdShellState.Leaving && Application.isPlaying && !leaving)
            {
                leaving = true;
                SceneManager.LoadSceneAsync("CutsceneB", LoadSceneMode.Single);
            }
        }

        private void RefreshProgress()
        {
            bootProgress.sizeDelta = new Vector2(flow.Config.ShellView.BootProgressSize.x * (float)Machine.BootProgress,
                flow.Config.ShellView.BootProgressSize.y);
            int remaining = Machine.SkipSecondsRemaining;
            bool ready = Machine.CanSkip;
            if (remaining == displayedCountdown && ready == displayedReady) return;
            skipLabel.text = remaining > 0 ? "跳过 " + remaining : "跳过";
            skipFill.color = ready ? Palette.Red : Palette.DarkRed;
            skipLabel.color = Palette.Bone;
            displayedCountdown = remaining; displayedReady = ready;
        }

        /// <summary>Consume shell clicks before any keyboard or stage input in the same frame.</summary>
        public bool HandlePointer(Vector2 screenPoint)
        {
            if (!isActiveAndEnabled || flow.IsApplicationPaused) return true;
            if (State == AdShellState.Boot || State == AdShellState.Leaving) return true;
            if (Contains(skipButton, screenPoint)) { RequestSkip(); return true; }
            if (Machine.IsModalOpen)
            {
                if (Contains(dialog.PrimaryButton, screenPoint))
                {
                    if (State == AdShellState.Lottery) AcceptLottery();
                    else if (State == AdShellState.ConfirmSkip) Stay();
                    else if (State == AdShellState.FailDialog) Retry();
                }
                else if (State == AdShellState.ConfirmSkip && Contains(dialog.SecondaryButton, screenPoint)) GiveUp();
                return true;
            }
            if (State == AdShellState.Idle && Contains(playButton, screenPoint)) { StartPlaying(); return true; }
            return false;
        }

        private bool Contains(RectTransform rect, Vector2 point) => rect != null && rect.gameObject.activeInHierarchy &&
            RectTransformUtility.RectangleContainsScreenPoint(rect, point, inputCamera);
    }
}
