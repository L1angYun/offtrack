using System;
using Offtrack.Core;
using UnityEngine;

namespace Offtrack.Runtime
{
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-100)]
    public sealed class GameFlow : MonoBehaviour
    {
        [SerializeField] private GameConfig config;
        [SerializeField] private TextAsset chartAsset;
        [SerializeField] private bool automaticTick = true;

        private IGameTimeSource timeSource;
        private bool hasApplicationFocus = true;
        private bool applicationPaused;
        private bool shellPaused;
        private bool needleInputEnabled = true;
        private bool runStartEnabled = true;

        public GameConfig Config => config;
        public GameSimulation Simulation { get; private set; }
        public ChartNote[] Chart { get; private set; } = Array.Empty<ChartNote>();
        public GameClock Clock { get; private set; }
        public RunState State => Simulation == null ? RunState.Idle : Simulation.State;
        public bool IsApplicationPaused => !hasApplicationFocus || applicationPaused || Time.timeScale <= 0f;
        public bool IsPaused => shellPaused || IsApplicationPaused;
        public bool ShellPaused => shellPaused;

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

        private void Update()
        {
            if (!automaticTick || Clock == null || Simulation == null) return;
            double delta = Clock.Tick(IsPaused || State != RunState.Playing);
            if (delta > 0.0) StepSimulation(delta);
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

        public void StartRun()
        {
            RequireSimulation();
            if (!runStartEnabled) return;
            Clock.Reset();
            Simulation.Start();
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
            MoveResult result = Simulation.Move(direction);
            if (result.Result == "jump") Changed?.Invoke();
            return result;
        }

        /// <summary>Explicit steps preserve their supplied duration for deterministic external driving.</summary>
        public void Advance(double deltaTime)
        {
            RequireSimulation();
            if (IsPaused || State != RunState.Playing) return;
            StepSimulation(deltaTime);
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
            RunEnded?.Invoke(result);
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
