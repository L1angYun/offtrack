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

        internal const double Step = ChartPlanner.Step;

        internal static void StartImmediately(GameFlow flow)
        {
            var shell = flow.GetComponent<AdShell>();
            if (shell.State == AdShellState.Guide)
            {
                Assert.That(shell.StartPlaying(), Is.True);
                flow.Advance(flow.PreparationLength);
            }
            else flow.StartRun();
            Assert.That(flow.Phase, Is.EqualTo(MachinePhase.Running));
        }

        /// <summary>A full-durability win: leave a track shortly before its next scratch arrives.</summary>
        internal static Press[] WinningPresses(GameFlow flow, double lead = .4)
        {
            var rules = flow.Config.ToRules();
            var result = ChartPlanner.Simulate(rules, flow.Chart, lead, out var plan);
            Assert.That(result.Won, Is.True, "The calibrated chart must be playable without damage.");
            Assert.That(result.Lives, Is.EqualTo(rules.Lives));
            return System.Array.ConvertAll(plan, press => new Press(press.Frame, press.Direction));
        }

        /// <summary>The damage a player who never presses takes, in order (the Judge logs each).</summary>
        internal static List<DamageEvent> NoInputDamage(GameFlow flow)
        {
            var damage = new List<DamageEvent>();
            ChartPlanner.Simulate(flow.Config.ToRules(), flow.Chart, null, out _, damage.Add);
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

    }
}
