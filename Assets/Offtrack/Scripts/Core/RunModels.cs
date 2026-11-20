using System;

namespace Offtrack.Core
{
    public enum RunState { Idle, Playing, Ended }

    public sealed class JumpState
    {
        public JumpState(int from, int to, double start, double end)
        { From = from; To = to; Start = start; End = end; }
        public int From { get; }
        public int To { get; }
        public double Start { get; }
        public double End { get; }
    }

    public sealed class ScratchState
    {
        internal ScratchState(int id, ChartNote note)
        { Id = id; Time = note.Time; Lane = note.Lane; EndTime = note.EndTime; HazardId = note.HazardId >= 0 ? note.HazardId : id; }
        public int Id { get; }
        public double Time { get; }
        public double EndTime { get; }
        public int HazardId { get; }
        public int Lane { get; }
        public bool Hit { get; internal set; }
        public bool Passed { get; internal set; }
    }

    public readonly struct MoveResult
    {
        public MoveResult(string result, int from, int to) { Result = result; From = from; To = to; }
        public string Result { get; }
        public int From { get; }
        public int To { get; }
    }

    public sealed class DamageEvent
    {
        public DamageEvent(int scratchId, int lane, string kind, int livesAfter, double time)
        { ScratchId = scratchId; Lane = lane; Kind = kind; LivesAfter = livesAfter; Time = time; }
        public int ScratchId { get; }
        public int Lane { get; }
        public string Kind { get; }
        public int LivesAfter { get; }
        public double Time { get; }
    }

    public readonly struct LandingEvent
    {
        public readonly int Lane;
        public readonly double Time;
        public LandingEvent(int lane, double time) { Lane = lane; Time = time; }
    }

    public sealed class RunResult
    {
        public RunResult(bool won, double time, int lives, int jumps, int damageCount, int lane)
        { Won = won; Time = time; Lives = lives; Jumps = jumps; DamageCount = damageCount; Lane = lane; }
        public bool Won { get; }
        public double Time { get; }
        public int Lives { get; }
        public int Jumps { get; }
        public int DamageCount { get; }
        public int Lane { get; }
    }

    /// <summary>Scoring for a visit to the three-level ad, with a checkpoint after each clear.</summary>
    public sealed class JourneyPerformance
    {
        private int clearedHits;
        private int attemptHits;
        private int accuracyHundredths = 10000;
        private bool finalized;

        public int Hits => clearedHits + attemptHits;
        public int Grade { get; private set; }
        public double Accuracy => accuracyHundredths / 100d;
        public bool SkippedGame { get; private set; }
        public bool EarnedGradeCelebration => finalized && accuracyHundredths > 9400;

        public void BeginAttempt() => attemptHits = 0;

        public void RecordHit() => attemptHits++;

        public void ClearLevel()
        {
            clearedHits += attemptHits;
            attemptHits = 0;
        }

        public void FinalizeResults(bool skippedGame = false, int unplayedLevelsOnAbandon = 0)
        {
            if (finalized) return;
            finalized = true;
            SkippedGame = skippedGame;
            if (skippedGame) accuracyHundredths = 0;
            var rolls = new Random();
            for (int hit = 0; hit < Hits; hit++)
            {
                if (hit < 7) Grade = Math.Min(59, Grade + rolls.Next(5, 11));
                if (!skippedGame) accuracyHundredths = Math.Max(0, accuracyHundredths - rolls.Next(150, 301));
            }
            accuracyHundredths = Math.Max(0, accuracyHundredths - 4000 * unplayedLevelsOnAbandon);
        }
    }
}
