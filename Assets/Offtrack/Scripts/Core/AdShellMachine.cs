using System;

namespace Offtrack.Core
{
    public enum AdShellState { Boot, Lottery, Idle, Playing, ConfirmSkip, FailDialog, Complete, Leaving }

    /// <summary>
    /// One Main-scene visit. Supply unscaled, real elapsed time independently of the game clock.
    /// UI and scene loading remain the runtime adapter's responsibility.
    /// </summary>
    public sealed class AdShellMachine
    {
        private readonly ShellRules rules;
        private double bootElapsed;
        private double skipElapsed;
        private double completeElapsed;

        public AdShellMachine(ShellRules configuration)
        {
            if (configuration == null) throw new ArgumentNullException(nameof(configuration));
            configuration.Validate();
            rules = configuration.Copy();
        }

        public AdShellState State { get; private set; } = AdShellState.Boot;
        public AdShellState PreviousState { get; private set; } = AdShellState.Boot;
        public double BootProgress => bootElapsed / rules.BootDuration;
        public int SkipSecondsRemaining => (int)Math.Ceiling(Math.Max(0, rules.SkipCountdownDuration - skipElapsed));
        public bool CanSkip => skipElapsed >= rules.SkipCountdownDuration && CanOpenConfirmation;
        public bool IsModalOpen => State == AdShellState.Lottery || State == AdShellState.ConfirmSkip
            || State == AdShellState.FailDialog;
        public double ResumeDelayRemaining { get; private set; }
        public bool AllowsNeedleInput => State == AdShellState.Playing && ResumeDelayRemaining <= 0;
        public bool AllowsStart => State == AdShellState.Idle;
        public bool AllowsRestart => ResumeDelayRemaining <= 0 && (State == AdShellState.Idle
            || State == AdShellState.Playing || State == AdShellState.Complete);
        public bool IsGamePaused => ResumeDelayRemaining > 0
            || (State == AdShellState.ConfirmSkip && PreviousState == AdShellState.Playing);

        public event Action<AdShellState> StateChanged;
        public event Action PauseChanged;

        private bool CanOpenConfirmation => State == AdShellState.Lottery || State == AdShellState.Idle
            || State == AdShellState.Playing || State == AdShellState.FailDialog || State == AdShellState.Complete;

        public void Advance(double realDelta) => Advance(realDelta, realDelta);

        /// <summary>
        /// Ad clocks use all real elapsed time. Recovery consumes only the foreground portion;
        /// adapters pass zero while the application is paused or unfocused.
        /// </summary>
        public void Advance(double realDelta, double recoveryDelta)
        {
            if (double.IsNaN(realDelta) || double.IsInfinity(realDelta) || realDelta < 0)
                throw new ArgumentOutOfRangeException(nameof(realDelta));
            if (double.IsNaN(recoveryDelta) || double.IsInfinity(recoveryDelta)
                || recoveryDelta < 0 || recoveryDelta > realDelta)
                throw new ArgumentOutOfRangeException(nameof(recoveryDelta));
            if (State == AdShellState.Leaving) return;

            if (State == AdShellState.Playing && ResumeDelayRemaining > 0)
            {
                ResumeDelayRemaining = Math.Max(0, ResumeDelayRemaining - recoveryDelta);
                if (ResumeDelayRemaining <= 0) PauseChanged?.Invoke();
            }

            if (State == AdShellState.Boot)
            {
                double consumed = Math.Min(realDelta, rules.BootDuration - bootElapsed);
                bootElapsed += consumed;
                realDelta -= consumed;
                if (bootElapsed < rules.BootDuration) return;
                Transition(AdShellState.Lottery);
            }

            // This clock begins once, when Lottery appears, and survives every retry and modal.
            skipElapsed += Math.Min(realDelta, rules.SkipCountdownDuration - skipElapsed);
            if (State != AdShellState.Complete) return;
            completeElapsed += Math.Min(realDelta, rules.CompleteDuration - completeElapsed);
            if (completeElapsed >= rules.CompleteDuration) Transition(AdShellState.Leaving);
        }

        public bool AcceptLottery()
        {
            if (State != AdShellState.Lottery) return false;
            Transition(AdShellState.Idle);
            return true;
        }

        public bool StartPlaying()
        {
            if (!AllowsStart) return false;
            completeElapsed = 0;
            Transition(AdShellState.Playing);
            return true;
        }

        public bool NotifyRunEnded(bool won)
        {
            if (State != AdShellState.Playing) return false;
            completeElapsed = 0;
            Transition(won ? AdShellState.Complete : AdShellState.FailDialog);
            return true;
        }

        public bool Retry()
        {
            if (State != AdShellState.FailDialog) return false;
            completeElapsed = 0;
            Transition(AdShellState.Idle);
            return true;
        }

        public bool RequestSkip()
        {
            if (!CanSkip) return false;
            PreviousState = State;
            Transition(AdShellState.ConfirmSkip);
            return true;
        }

        public bool Stay()
        {
            if (State != AdShellState.ConfirmSkip) return false;
            ResumeDelayRemaining = PreviousState == AdShellState.Playing ? rules.StayResumeDelay : 0;
            Transition(PreviousState);
            return true;
        }

        public bool GiveUp()
        {
            if (State != AdShellState.ConfirmSkip) return false;
            Transition(AdShellState.Leaving);
            return true;
        }

        /// <summary>Developer R shortcut: immediately restart a non-modal run; never dismiss a dialog.</summary>
        public bool Restart()
        {
            if (!AllowsRestart) return false;
            completeElapsed = 0;
            Transition(AdShellState.Playing);
            return true;
        }

        private void Transition(AdShellState state)
        {
            if (State == state) return;
            State = state;
            StateChanged?.Invoke(state);
        }
    }
}
