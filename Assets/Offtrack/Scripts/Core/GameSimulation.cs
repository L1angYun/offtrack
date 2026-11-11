using System;
using System.Collections.Generic;

namespace Offtrack.Core
{
    public sealed class GameSimulation
    {
        private readonly RuleConfig rules;
        private readonly ScratchState[] scratches;

        public GameSimulation(RuleConfig config, IReadOnlyList<ChartNote> chart)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));
            config.Validate();
            ChartTools.Validate(chart, config.LaneCount);
            rules = config.Copy();
            scratches = new ScratchState[chart.Count];
            for (int i = 0; i < chart.Count; i++) scratches[i] = new ScratchState(i, chart[i]);
            Notes = Array.AsReadOnly(scratches);
            Reset(RunState.Idle);
        }
        public double Time { get; private set; }
        public int Lane { get; private set; }
        public int Lives { get; private set; }
        public int Jumps { get; private set; }
        public int DamageCount { get; private set; }
        public double ReadyAt { get; private set; }
        public double InvincibleUntil { get; private set; }
        public double LastLanding { get; private set; }
        public JumpState Jump { get; private set; }
        public IReadOnlyList<ScratchState> Notes { get; }
        public RunState State { get; private set; }
        public RunResult Result { get; private set; }
        public event Action<DamageEvent> DamageTaken;
        public event Action<RunResult> Ended;
        public void Start() => Reset(RunState.Playing);

        public MoveResult Move(int dir)
        {
            if (dir != -1 && dir != 1) throw new ArgumentOutOfRangeException(nameof(dir), "Direction must be -1 or +1.");
            if (State != RunState.Playing) return new MoveResult("inactive", Lane, Lane);
            if (Jump != null) return new MoveResult("airborne", Lane, Lane);
            if (Time < ReadyAt) return new MoveResult("cooldown", Lane, Lane);
            int target = Lane + dir;
            if (target < 0 || target >= rules.LaneCount) return new MoveResult("edge", Lane, Lane);

            Jump = new JumpState(Lane, target, Time, Time + rules.JumpDuration);
            // Keep the reference's left-to-right additions for double-precision replay parity.
            ReadyAt = Time + rules.JumpDuration + rules.SettleDuration;
            Jumps++;
            return new MoveResult("jump", Lane, target);
        }

        public void Step(double dt)
        {
            if (double.IsNaN(dt) || double.IsInfinity(dt) || dt < 0)
                throw new ArgumentOutOfRangeException(nameof(dt), "Step duration must be finite and nonnegative.");
            if (State != RunState.Playing) return;

            Time = Math.Min(rules.Duration, Time + dt);
            if (Jump != null && Time >= Jump.End)
            {
                Lane = Jump.To;
                LastLanding = Jump.End;
                Jump = null;
            }

            foreach (var scratch in scratches)
            {
                if (scratch.Hit || scratch.Passed) continue;
                double window = JudgeRules.WindowFor(scratch.Time, rules);
                if (Time > scratch.Time + window)
                {
                    scratch.Passed = true;
                    continue;
                }
                if (Math.Abs(Time - scratch.Time) <= window && Jump == null && Lane == scratch.Lane && Time >= InvincibleUntil)
                {
                    TakeHit(scratch);
                    if (State != RunState.Playing) break;
                }
            }
            if (State == RunState.Playing && Time >= rules.Duration) Finish(true);
        }

        private void Reset(RunState state)
        {
            Time = 0;
            Lane = rules.StartLane;
            Lives = rules.Lives;
            Jumps = 0;
            DamageCount = 0;
            ReadyAt = 0;
            InvincibleUntil = 0;
            LastLanding = -100;
            Jump = null;
            Result = null;
            foreach (var scratch in scratches)
            {
                scratch.Hit = false;
                scratch.Passed = false;
            }
            State = state;
        }

        private void TakeHit(ScratchState scratch)
        {
            scratch.Hit = true;
            Lives--;
            DamageCount++;
            InvincibleUntil = Time + rules.HitInvincibility;
            var damage = new DamageEvent(scratch.Id, scratch.Lane,
                JudgeRules.ClassifyLanding(Time, LastLanding, rules.EarlyLandingThreshold), Lives, Time);
            DamageTaken?.Invoke(damage);
            if (Lives <= 0) Finish(false);
        }

        private void Finish(bool won)
        {
            State = RunState.Ended;
            Jump = null;
            Result = new RunResult(won, Time, Lives, Jumps, DamageCount, Lane);
            Ended?.Invoke(Result);
        }
    }
}
