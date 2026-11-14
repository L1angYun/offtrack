using System;
using Offtrack.Core;
using UnityEngine;

namespace Offtrack.Runtime
{
    /// <summary>The turntable's mechanical phase around the rule simulation.</summary>
    public enum MachinePhase { Parked, Starting, Running, Stopping, Stopped }

    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-100)]
    public sealed class GameFlow : MonoBehaviour
    {
        [SerializeField] private GameConfig config;
        [SerializeField] private TextAsset chartAsset;
        [SerializeField] private bool automaticTick = true;

        private IGameTimeSource timeSource;
        private IMusicClock musicClock;
        private bool hasApplicationFocus = true;
        private bool applicationPaused;
        private bool shellPaused;
        private bool needleInputEnabled = true;
        private bool runStartEnabled = true;
        private MachinePhase phase = MachinePhase.Parked;
        private double phaseElapsed;
        private double preparationLength;
        private double endTime;

        public GameConfig Config => config;
        public GameSimulation Simulation { get; private set; }
        public ChartNote[] Chart { get; private set; } = Array.Empty<ChartNote>();
        public GameClock Clock { get; private set; }
        public RunState State => Simulation == null ? RunState.Idle : Simulation.State;
        public bool IsApplicationPaused => !hasApplicationFocus || applicationPaused || Time.timeScale <= 0f;
        public bool IsPaused => shellPaused || IsApplicationPaused;
        public bool ShellPaused => shellPaused;

        public MachinePhase Phase => phase;
        /// <summary>Seconds spent in the current mechanical phase; frozen while paused.</summary>
        public double PhaseElapsed => phaseElapsed;
        public double PhaseProgress => phase == MachinePhase.Starting ? Fraction(phaseElapsed, preparationLength)
            : phase == MachinePhase.Stopping ? Fraction(phaseElapsed, StopDuration)
            : phase == MachinePhase.Stopped ? 1 : 0;
        /// <summary>Unpaused seconds advanced by this flow; drives short presentation responses.</summary>
        public double PresentationTime { get; private set; }
        /// <summary>Needle-drop length used by the current run; zero for an immediate start.</summary>
        public double PreparationLength => preparationLength;
        public bool IsTransitioning => phase == MachinePhase.Starting || phase == MachinePhase.Stopping;
        public bool CanStartRun => runStartEnabled && !IsTransitioning;
        /// <summary>Run time for presentation: negative during the needle drop, then the judge clock.</summary>
        public double RunTime => phase == MachinePhase.Starting ? phaseElapsed - preparationLength
            : Simulation == null ? 0 : Simulation.Time;
        public double StopDuration => config == null ? 0 : config.Machine.StopDuration;

        /// <summary>
        /// Platter angle in degrees (counter-clockwise positive). Running uses exactly the judge
        /// clock. The needle drop accelerates uniformly from rest so that the platter reaches full
        /// speed and angle zero at judge time zero; every ending decelerates uniformly to rest.
        /// </summary>
        public double DiscRotationDegrees
        {
            get
            {
                if (config == null || Simulation == null) return 0;
                double omega = 360.0 / (config.BeatsPerRevolution * (60.0 / config.Bpm));
                switch (phase)
                {
                    case MachinePhase.Starting:
                        return -omega * (phaseElapsed * phaseElapsed / (2 * preparationLength) - preparationLength / 2);
                    case MachinePhase.Running:
                        return -omega * Simulation.Time;
                    case MachinePhase.Stopping:
                        double stop = StopDuration;
                        if (stop <= 0) return -omega * endTime;
                        return -omega * (endTime + phaseElapsed - phaseElapsed * phaseElapsed / (2 * stop));
                    case MachinePhase.Stopped:
                        return -omega * (endTime + StopDuration / 2);
                    default:
                        return omega * config.Machine.StartDuration / 2;
                }
            }
        }

        public bool AutomaticTick
        {
            get => automaticTick;
            set
            {
                if (automaticTick == value) return;
                automaticTick = value;
                Clock?.ResetSample();
            }
        }

        public event Action RunStarted;
        public event Action<RunResult> RunEnded;
        public event Action<DamageEvent> DamageTaken;
        public event Action Changed;
        /// <summary>Every needle request that reached the machine, with its actual rule result.</summary>
        public event Action<int, MoveResult> MoveAttempted;
        /// <summary>Raised once the machine has fully stopped after an ending, win or loss alike.</summary>
        public event Action<RunResult> RunStopped;
        public event Action<MachinePhase> PhaseChanged;

        private void Awake()
        {
            if (config != null && chartAsset != null) Configure(config, chartAsset);
        }

        private void OnEnable() => Clock?.ResetSample();
        private void Start() => Changed?.Invoke();
        private void OnDisable() => Clock?.ResetSample();

        private void OnDestroy()
        {
            DetachSimulation();
        }

        /// <summary>True when the last automatic update followed the music position (GDD 9.3).</summary>
        public bool FollowingMusic { get; private set; }

        /// <summary>The music becomes the clock while running; frame time remains the fallback.</summary>
        public void SetMusicClock(IMusicClock clock) => musicClock = clock;

        private void Update()
        {
            if (!automaticTick || Clock == null || Simulation == null) return;
            FollowingMusic = false;
            // A test time source is authoritative; otherwise the heard music drives the judge, so
            // no frame rate or slow frame can make the scratches drift from the beat.
            if (timeSource == null && musicClock != null && !IsPaused && phase == MachinePhase.Running
                && State == RunState.Playing && musicClock.TryGetPosition(out double position))
            {
                FollowingMusic = true;
                Clock.ResetSample();
                double target = Math.Min(position, config.Duration);
                // Catch up in bounded steps so every judge window is still sampled.
                for (int guard = 0; guard < 10000 && phase == MachinePhase.Running && State == RunState.Playing; guard++)
                {
                    double remaining = target - Simulation.Time;
                    if (remaining <= 1e-9) break;
                    StepPhase(Math.Min(remaining, config.MaximumFrameDelta));
                }
                return;
            }
            double delta = Clock.Tick(IsPaused || !IsAdvancing);
            if (delta > 0.0) StepPhase(delta);
        }

        public void Configure(GameConfig gameConfig, TextAsset chart)
        {
            if (gameConfig == null) throw new ArgumentNullException(nameof(gameConfig));
            RuleConfig rules = gameConfig.ToRules();
            ChartNote[] notes = ChartService.Load(chart, rules.LaneCount);
            var nextSimulation = new GameSimulation(rules, notes);
            var nextClock = new GameClock(gameConfig, timeSource);

            DetachSimulation();
            config = gameConfig;
            chartAsset = chart;
            Chart = notes;
            Simulation = nextSimulation;
            Clock = nextClock;
            Simulation.DamageTaken += HandleDamage;
            Simulation.Ended += HandleEnded;
            phaseElapsed = 0;
            preparationLength = 0;
            endTime = 0;
            SetPhase(MachinePhase.Parked);
            Changed?.Invoke();
        }

        public void SetTimeSource(IGameTimeSource source)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            timeSource = source;
            if (config == null) return;
            Clock = new GameClock(config, source);
            if (Simulation != null) Clock.Synchronize(Simulation.Time);
        }

        /// <summary>Starts judging immediately (rule tests, fixtures and the shell-less M1 path).</summary>
        public void StartRun() => StartRun(false);

        /// <summary>
        /// Starts a run. With <paramref name="prepare"/>, the needle drops and the platter spins up
        /// first; the judge clock stays at zero until the needle has landed.
        /// </summary>
        public void StartRun(bool prepare)
        {
            RequireSimulation();
            if (!CanStartRun) return;
            Clock.Reset();
            Simulation.Start();
            preparationLength = prepare ? config.Machine.StartDuration : 0;
            phaseElapsed = 0;
            endTime = 0;
            SetPhase(preparationLength > 0 ? MachinePhase.Starting : MachinePhase.Running);
            RunStarted?.Invoke();
            Changed?.Invoke();
        }

        public void ResetToIdle()
        {
            RequireSimulation();
            // Recreate only the rule session; the scene and the 32 scratch objects stay in place.
            Configure(config, chartAsset);
        }

        public void SetShellPaused(bool paused)
        {
            if (shellPaused == paused) return;
            shellPaused = paused;
            Clock?.ResetSample();
        }

        public void SetNeedleInputEnabled(bool enabled) => needleInputEnabled = enabled;
        internal void SetRunStartEnabled(bool enabled) => runStartEnabled = enabled;

        public MoveResult Move(int direction)
        {
            RequireSimulation();
            if (IsPaused || !needleInputEnabled) return new MoveResult("inactive", Simulation.Lane, Simulation.Lane);
            if (phase != MachinePhase.Running)
            {
                var refused = new MoveResult("inactive", Simulation.Lane, Simulation.Lane);
                // A press during the needle drop still gets the machine's "not yet" response.
                if (phase == MachinePhase.Starting) MoveAttempted?.Invoke(direction, refused);
                return refused;
            }
            MoveResult result = Simulation.Move(direction);
            MoveAttempted?.Invoke(direction, result);
            Changed?.Invoke();
            return result;
        }

        /// <summary>Explicit steps preserve their supplied duration for deterministic external driving.</summary>
        public void Advance(double deltaTime)
        {
            RequireSimulation();
            if (double.IsNaN(deltaTime) || double.IsInfinity(deltaTime) || deltaTime < 0)
                throw new ArgumentOutOfRangeException(nameof(deltaTime));
            if (IsPaused || !IsAdvancing || deltaTime <= 0) return;
            StepPhase(deltaTime);
        }

        public void SetApplicationFocus(bool focused)
        {
            hasApplicationFocus = focused;
            Clock?.ResetSample();
        }

        private void OnApplicationFocus(bool focused) => SetApplicationFocus(focused);

        private void OnApplicationPause(bool paused)
        {
            applicationPaused = paused;
            Clock?.ResetSample();
        }

        private bool IsAdvancing => phase == MachinePhase.Starting || phase == MachinePhase.Stopping
            || (phase == MachinePhase.Running && State == RunState.Playing);

        private void StepPhase(double deltaTime)
        {
            PresentationTime += deltaTime;
            switch (phase)
            {
                case MachinePhase.Starting:
                    phaseElapsed += deltaTime;
                    // The surplus beyond the drop is discarded, so judge time zero is observable.
                    if (phaseElapsed >= preparationLength)
                    {
                        phaseElapsed = 0;
                        SetPhase(MachinePhase.Running);
                    }
                    Clock.Synchronize(Simulation.Time);
                    Changed?.Invoke();
                    break;
                case MachinePhase.Running:
                    StepSimulation(deltaTime);
                    break;
                case MachinePhase.Stopping:
                    phaseElapsed = Math.Min(StopDuration, phaseElapsed + deltaTime);
                    Clock.Synchronize(Simulation.Time);
                    if (phaseElapsed >= StopDuration) FinishStopping();
                    Changed?.Invoke();
                    break;
            }
        }

        private void FinishStopping()
        {
            phaseElapsed = StopDuration;
            SetPhase(MachinePhase.Stopped);
            RunStopped?.Invoke(Simulation.Result);
        }

        private void SetPhase(MachinePhase next)
        {
            if (phase == next) return;
            phase = next;
            PhaseChanged?.Invoke(next);
        }

        private static double Fraction(double value, double total) =>
            total <= 0 ? 1 : Math.Min(1, Math.Max(0, value / total));

        private void StepSimulation(double deltaTime)
        {
            Simulation.Step(deltaTime);
            Clock.Synchronize(Simulation.Time);
            Changed?.Invoke();
        }

        private void HandleDamage(DamageEvent damage)
        {
            Clock.Synchronize(Simulation.Time);
            DamageTaken?.Invoke(damage);
        }

        private void HandleEnded(RunResult result)
        {
            Clock.Synchronize(Simulation.Time);
            Clock.ResetSample();
            endTime = Simulation.Time;
            phaseElapsed = 0;
            // The remainder of the ending step is discarded; the stop begins on the next sample.
            SetPhase(MachinePhase.Stopping);
            RunEnded?.Invoke(result);
            if (phase == MachinePhase.Stopping && StopDuration <= 0) FinishStopping();
        }

        private void RequireSimulation()
        {
            if (Simulation == null)
                throw new InvalidOperationException("Configure GameFlow with a GameConfig and chart before using it.");
        }

        private void DetachSimulation()
        {
            if (Simulation == null) return;
            Simulation.DamageTaken -= HandleDamage;
            Simulation.Ended -= HandleEnded;
        }
    }
}
