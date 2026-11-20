using System.Collections;
using System.Linq;
using NUnit.Framework;
using Offtrack.Core;
using Offtrack.Runtime;
using Offtrack.Runtime.Presentation;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Offtrack.Tests.PlayMode
{
    public sealed class V3RecordGuardTests
    {
        [UnityTest]
        public IEnumerator MechanicalFramesUseTheStrictPalette()
        {
            yield return SceneManager.LoadSceneAsync(OfftrackScenes.Main);
            var shell = Object.FindAnyObjectByType<AdShell>();
            var flow = shell.Flow;
            shell.AutomaticTick = flow.AutomaticTick = false;
            flow.SetApplicationFocus(true);
            shell.Advance(flow.Config.Shell.BootDuration);
            Assert.That(shell.AcceptLottery(), Is.True);
            flow.SelectLevel(2);
            Assert.That(shell.StartPlaying(), Is.True);
            flow.Advance(flow.PreparationLength / 2);
            yield return V3Acceptance.StrictRecordFrame("needle drop", false);
            flow.Advance(flow.PreparationLength / 2 + .00001);
            var pool = Object.FindAnyObjectByType<ScratchPool>();
            var runner = flow.Record.Hazards.First(hazard => hazard.kind == HazardKind.RunningScratch);
            var gap = flow.Record.Hazards.First(hazard => hazard.kind == HazardKind.Break);
            var damage = ChartBot.NoInputDamage(flow)[0];
            AdvanceTo(damage.Time + .001);
            Assert.That(flow.Simulation.DamageCount, Is.EqualTo(1));
            yield return V3Acceptance.StrictRecordFrame("first impact", true);
            yield return V3Acceptance.AntialiasedRecordFrame("first impact");
            AdvanceTo(damage.Time + .101);
            yield return V3Acceptance.StrictRecordFrame("100 ms after impact", false);

            flow.SelectLevel(2);
            ChartBot.StartImmediately(flow);
            foreach (var sample in new[] { (runner.phase - 2.5, "RunnerTail", false), (gap.phase - .5, "BreakStart", true) }.OrderBy(sample => sample.Item1))
            {
                AdvanceTo(sample.Item1 * flow.Record.BeatSeconds);
                Assert.That(pool.Items.Any(item => item.transform.Find(sample.Item2)?.gameObject.activeInHierarchy == true), Is.True, sample.Item2);
                yield return V3Acceptance.StrictRecordFrame(sample.Item2, sample.Item3);
            }

            flow.SelectLevel(3);
            ChartBot.StartImmediately(flow);
            var arrow = GameObject.Find("ReversalStamp").transform.Find("Arrow");
            double flip = (flow.Record.Reversals[0] - 2) * flow.Record.BeatSeconds;
            AdvanceTo(flip - .001);
            float before = arrow.localEulerAngles.z;
            AdvanceTo(flip + .001);
            Assert.That(Mathf.Abs(Mathf.DeltaAngle(before, arrow.localEulerAngles.z)), Is.EqualTo(180).Within(.01));
            yield return V3Acceptance.StrictRecordFrame("reverse announced", false);

            void AdvanceTo(double time)
            {
                Assert.That(time, Is.GreaterThan(flow.RunTime));
                flow.Advance(time - flow.RunTime);
            }
        }
    }
}
