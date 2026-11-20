using System;
using System.Collections.Generic;

namespace Offtrack.Core
{
    public readonly struct PlannedPress
    {
        public readonly int Frame;
        public readonly int Direction;
        public readonly int ContactIndex;
        public PlannedPress(int frame, int direction, int contactIndex)
        { Frame = frame; Direction = direction; ContactIndex = contactIndex; }
    }

    public static class ChartPlanner
    {
        public const double Step = 1.0 / 60;

        public static RunResult Simulate(RuleConfig rules, IReadOnlyList<ChartNote> chart, double? lead,
            out PlannedPress[] presses, Action<DamageEvent> onDamage = null)
        {
            var simulation = new GameSimulation(rules, chart);
            var plan = new List<PlannedPress>();
            simulation.DamageTaken += onDamage;
            simulation.Start();
            for (int frame = 0; simulation.State == RunState.Playing; frame++)
            {
                var threat = lead.HasValue && simulation.Jump == null && simulation.Time >= simulation.ReadyAt
                    ? NextOn(simulation, rules, simulation.Lane, simulation.Time) : null;
                if (threat != null && threat.Time - simulation.Time <= lead.Value)
                {
                    int best = 0;
                    double bestTime = double.NegativeInfinity;
                    for (int direction = -1; direction <= 1; direction += 2)
                    {
                        int lane = simulation.Lane + direction;
                        if (lane < 0 || lane >= rules.LaneCount) continue;
                        double next = NextOn(simulation, rules, lane, simulation.Time + rules.JumpDuration)?.Time ?? double.PositiveInfinity;
                        if (next > bestTime) { bestTime = next; best = direction; }
                    }
                    if (best != 0 && simulation.Move(best).Result == "jump") plan.Add(new PlannedPress(frame, best, threat.Id));
                }
                simulation.Step(Step);
            }
            presses = plan.ToArray();
            return simulation.Result;
        }

        private static ScratchState NextOn(GameSimulation simulation, RuleConfig rules, int lane, double after)
        {
            foreach (var note in simulation.Notes)
            {
                if (note.Lane != lane || note.Hit || note.Passed) continue;
                if (note.EndTime + JudgeRules.WindowFor(note.EndTime, rules) >= after) return note;
            }
            return null;
        }
    }
}
