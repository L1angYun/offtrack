using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using Offtrack.Core;
using UnityEngine;

namespace Offtrack.Tests.EditMode
{
    public sealed class FixtureReplayTests
    {
        internal static string ProjectRoot => Directory.GetParent(Application.dataPath).FullName;

        private static IEnumerable<TestCaseData> FixtureCases()
        {
            string directory = Path.Combine(ProjectRoot, "Assets/Offtrack/Tests/EditMode/Fixtures");
            var files = Directory.GetFiles(directory, "*.json");
            System.Array.Sort(files, System.StringComparer.Ordinal);
            foreach (string path in files)
                yield return new TestCaseData(path).SetName("PrototypeParity_" + Path.GetFileNameWithoutExtension(path));
        }

        [TestCaseSource(nameof(FixtureCases))]
        public void ReplayMatchesEveryMoveHitAndFinalFrame(string path)
        {
            var fixture = ReadObject(File.ReadAllText(path));
            var constants = Obj(fixture["constants"]);
            var stepping = Obj(fixture["stepping"]);
            Assert.That((string)stepping["order"], Does.Contain("apply inputs").And.Contain("then advance"));
            var simulation = new GameSimulation(Rules(constants), ChartTools.FromBeats(Number(constants, "bpm"), Beats(fixture), 3));
            var inputs = Array(fixture["inputs"]);
            var expected = Obj(fixture["expected"]);
            var moves = Array(expected["moves"]);
            var hits = Array(expected["hits"]);
            var ending = Obj(expected["end"]);
            int frame = -1, inputIndex = 0, hitIndex = 0, endFrame = -1, endedEvents = 0;
            simulation.DamageTaken += damage =>
            {
                Assert.That(hitIndex, Is.LessThan(hits.Count), "Unexpected damage event");
                var want = Obj(hits[hitIndex++]);
                Assert.That(frame, Is.EqualTo(Integer(want, "frame")));
                Assert.That(damage.ScratchId, Is.EqualTo(Integer(want, "scratchId")));
                Assert.That(damage.Lane, Is.EqualTo(Integer(want, "lane")));
                Assert.That(damage.Kind, Is.EqualTo((string)want["kind"]));
                Assert.That(damage.LivesAfter, Is.EqualTo(Integer(want, "livesAfter")));
                Assert.That(damage.Time, Is.EqualTo(Number(want, "time")).Within(1e-9));
            };
            simulation.Ended += result => { endFrame = frame; endedEvents++; };
            simulation.Start();
            for (frame = 0; frame <= Integer(ending, "frame") + 1 && simulation.State == RunState.Playing; frame++)
            {
                while (inputIndex < inputs.Count && Integer(Obj(inputs[inputIndex]), "frame") == frame)
                {
                    var input = Obj(inputs[inputIndex]);
                    var want = Obj(moves[inputIndex++]);
                    Assert.That(frame, Is.EqualTo(Integer(want, "frame")));
                    Assert.That(simulation.Time, Is.EqualTo(Number(want, "time")).Within(1e-9));
                    var actual = simulation.Move(Integer(input, "dir"));
                    Assert.That(actual.Result, Is.EqualTo((string)want["result"]), "move " + (inputIndex - 1));
                    Assert.That(actual.From, Is.EqualTo(Integer(want, "from")));
                    Assert.That(actual.To, Is.EqualTo(Integer(want, "to")));
                }
                simulation.Step(Number(stepping, "dt"));
            }
            Assert.That(inputIndex, Is.EqualTo(inputs.Count));
            Assert.That(inputIndex, Is.EqualTo(moves.Count));
            Assert.That(hitIndex, Is.EqualTo(hits.Count));
            Assert.That(endedEvents, Is.EqualTo(1));
            Assert.That(endFrame, Is.EqualTo(Integer(ending, "frame")));
            Assert.That(simulation.State, Is.EqualTo(RunState.Ended));
            var actualEnd = simulation.Result;
            Assert.That(actualEnd, Is.Not.Null);
            Assert.That(actualEnd.Won, Is.EqualTo((bool)ending["won"]));
            Assert.That(actualEnd.Time, Is.EqualTo(Number(ending, "time")).Within(1e-9));
            Assert.That(actualEnd.Lives, Is.EqualTo(Integer(ending, "lives")));
            Assert.That(actualEnd.Jumps, Is.EqualTo(Integer(ending, "jumps")));
            Assert.That(actualEnd.DamageCount, Is.EqualTo(Integer(ending, "damageCount")));
            Assert.That(actualEnd.Lane, Is.EqualTo(Integer(ending, "lane")));
        }

        [Test]
        public void FixtureSetContainsAllTwelveBaselines()
        {
            Assert.That(Directory.GetFiles(Path.Combine(ProjectRoot, "Assets/Offtrack/Tests/EditMode/Fixtures"), "*.json"), Has.Length.EqualTo(12));
        }

        internal static Dictionary<string, object> ReadObject(string text) => Obj(FixtureJson.Parse(text));
        internal static Dictionary<string, object> Obj(object value) => (Dictionary<string, object>)value;
        internal static List<object> Array(object value) => (List<object>)value;
        internal static double Number(Dictionary<string, object> value, string key) => (double)value[key];
        internal static int Integer(Dictionary<string, object> value, string key) => (int)Number(value, key);

        internal static List<BeatNote> Beats(Dictionary<string, object> fixture)
        {
            var result = new List<BeatNote>();
            foreach (var item in Array(fixture["chartBeats"]))
            {
                var pair = Array(item);
                result.Add(new BeatNote((double)pair[0], (int)(double)pair[1]));
            }
            return result;
        }

        private static RuleConfig Rules(Dictionary<string, object> value) => new RuleConfig
        {
            LaneCount = 3, StartLane = Integer(value, "startLane"), Lives = Integer(value, "lives"),
            JumpDuration = Number(value, "jump"), SettleDuration = Number(value, "settle"),
            HitInvincibility = Number(value, "hitInvincible"), EarlyWindow = Number(value, "windowBefore12s"),
            LateWindow = Number(value, "windowFrom12s"), WindowSwitchTime = 12,
            EarlyLandingThreshold = Number(value, "earlyThreshold"), Duration = Number(value, "duration"),
            Bpm = Number(value, "bpm"), BeatsPerRevolution = 12
        };
    }
}
