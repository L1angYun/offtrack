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
        internal ScratchState(int id, ChartNote note) { Id = id; Time = note.Time; Lane = note.Lane; }
        public int Id { get; }
        public double Time { get; }
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
}
