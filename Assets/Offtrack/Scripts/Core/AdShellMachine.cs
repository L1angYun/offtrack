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
        public bool AllowsNeedleInput => State == AdShellState.Playing;
        public bool AllowsStart => State == AdShellState.Idle;
        public bool AllowsRestart => State == AdShellState.Idle || State == AdShellState.Playing
            || State == AdShellState.Complete;
        public bool IsGamePaused => State == AdShellState.ConfirmSkip && PreviousState == AdShellState.Playing;

        public event Action<AdShellState> StateChanged;

        private bool CanOpenConfirmation => State == AdShellState.Lottery || State == AdShellState.Idle
            || State == AdShellState.Playing || State == AdShellState.FailDialog || State == AdShellState.Complete;

        public void Advance(double realDelta)
        {
            if (double.IsNaN(realDelta) || double.IsInfinity(realDelta) || realDelta < 0)
                throw new ArgumentOutOfRangeException(nameof(realDelta));
            if (State == AdShellState.Leaving) return;

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
