using System.Collections.Generic;
using NUnit.Framework;
using Offtrack.Core;
using Offtrack.Runtime;

namespace Offtrack.Tests.PlayMode
{
    /// <summary>
    /// Plans inputs for whatever chart the scene carries (M4: the side-A chart replaced the
    /// prototype chart that bot_early.json was recorded against). Plans are simulated with the
    /// same rules and the same 1/60 s steps the tests use, so a plan that wins here wins in-scene.
    /// </summary>
    internal static class ChartBot
    {
        internal readonly struct Press
        {
            public Press(int frame, int direction) { Frame = frame; Direction = direction; }
            public int Frame { get; }
            public int Direction { get; }
        }

        internal const double Step = 1.0 / 60.0;

        /// <summary>A full-durability win: leave a track shortly before its next scratch arrives.</summary>
        internal static Press[] WinningPresses(GameFlow flow, double lead = .4)
        {
            var rules = flow.Config.ToRules();
            var sim = new GameSimulation(rules, flow.Chart);
            sim.Start();
            var presses = new List<Press>();
            for (int frame = 0; frame < 100000 && sim.State == RunState.Playing; frame++)
            {
                if (sim.Jump == null && sim.Time >= sim.ReadyAt && NextOn(sim, rules, sim.Lane, sim.Time) - sim.Time <= lead)
                {
                    int best = 0;
                    double bestTime = double.NegativeInfinity;
                    foreach (int direction in new[] { -1, 1 })
                    {
                        int lane = sim.Lane + direction;
                        if (lane < 0 || lane >= rules.LaneCount) continue;
                        double next = NextOn(sim, rules, lane, sim.Time + rules.JumpDuration);
                        if (next > bestTime) { bestTime = next; best = direction; }
                    }
                    if (best != 0 && sim.Move(best).Result == "jump") presses.Add(new Press(frame, best));
                }
                sim.Step(Step);
            }
            Assert.That(sim.Result, Is.Not.Null);
            Assert.That(sim.Result.Won, Is.True, "The calibrated chart must be playable without damage.");
            Assert.That(sim.Result.Lives, Is.EqualTo(rules.Lives));
            return presses.ToArray();
        }

        /// <summary>The damage a player who never presses takes, in order (the Judge logs each).</summary>
        internal static List<DamageEvent> NoInputDamage(GameFlow flow)
        {
            var sim = new GameSimulation(flow.Config.ToRules(), flow.Chart);
            var damage = new List<DamageEvent>();
            sim.DamageTaken += damage.Add;
            sim.Start();
            for (int frame = 0; frame < 100000 && sim.State == RunState.Playing; frame++) sim.Step(Step);
            return damage;
        }

        /// <summary>Drives the scene's flow with a plan; every planned press must be a jump.</summary>
        internal static int Play(GameFlow flow, Press[] presses, bool requireJumps = true)
        {
            int index = 0;
            for (int frame = 0; frame < 100000 && flow.State == RunState.Playing; frame++)
            {
                while (index < presses.Length && presses[index].Frame == frame)
                {
                    var result = flow.Move(presses[index].Direction);
                    if (requireJumps) Assert.That(result.Result, Is.EqualTo("jump"), "frame " + frame);
                    index++;
                }
                flow.Advance(Step);
            }
            return index;
        }

        private static double NextOn(GameSimulation sim, RuleConfig rules, int lane, double after)
        {
            foreach (var note in sim.Notes)
            {
                if (note.Lane != lane || note.Hit || note.Passed) continue;
                if (note.Time + JudgeRules.WindowFor(note.Time, rules) >= after) return note.Time;
            }
            return double.PositiveInfinity;
        }
    }
}
