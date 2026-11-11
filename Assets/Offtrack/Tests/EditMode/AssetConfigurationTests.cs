using System.IO;
using NUnit.Framework;
using Offtrack.Core;
using Offtrack.Runtime;
using UnityEditor;

namespace Offtrack.Tests.EditMode
{
    public sealed class AssetConfigurationTests
    {
        [Test]
        public void GeneratedConfigurationMatchesTheM1TuningTable()
        {
            var config = AssetDatabase.LoadAssetAtPath<GameConfig>("Assets/Offtrack/Config/GameConfig.asset");
            Assert.That(config, Is.Not.Null, "Run Offtrack.Editor.M1Builder.BuildAll to generate M1 assets.");
            var rules = config.ToRules();
            Assert.That(rules.LaneCount, Is.EqualTo(3));
            Assert.That(rules.StartLane, Is.EqualTo(1));
            Assert.That(rules.Lives, Is.EqualTo(4));
            Assert.That(rules.JumpDuration, Is.EqualTo(0.22));
            Assert.That(rules.SettleDuration, Is.EqualTo(0.35));
            Assert.That(rules.HitInvincibility, Is.EqualTo(0.65));
            Assert.That(rules.EarlyWindow, Is.EqualTo(0.115));
            Assert.That(rules.LateWindow, Is.EqualTo(0.145));
            Assert.That(rules.WindowSwitchTime, Is.EqualTo(12));
            Assert.That(rules.EarlyLandingThreshold, Is.EqualTo(0.2));
            Assert.That(rules.Duration, Is.EqualTo(36));
            Assert.That(rules.Bpm, Is.EqualTo(112));
            Assert.That(rules.BeatsPerRevolution, Is.EqualTo(12));
            Assert.That(config.MaximumFrameDelta, Is.EqualTo(0.04));
        }

        [Test]
        public void LandedChartJsonMatchesFixtureBeatsWithinOneTenThousandthSecond()
        {
            var fixture = FixtureReplayTests.ReadObject(File.ReadAllText(Path.Combine(FixtureReplayTests.ProjectRoot, "Assets/Offtrack/Tests/EditMode/Fixtures/no_input.json")));
            var beats = FixtureReplayTests.Beats(fixture);
            var chartJson = FixtureJson.Parse(File.ReadAllText(Path.Combine(FixtureReplayTests.ProjectRoot, "Assets/Offtrack/Charts/side-a.json")));
            var rows = FixtureReplayTests.Array(chartJson);
            Assert.That(rows, Has.Count.EqualTo(22));
            Assert.That(rows, Has.Count.EqualTo(beats.Count));
            var constants = FixtureReplayTests.Obj(fixture["constants"]);
            double secondsPerBeat = 60 / FixtureReplayTests.Number(constants, "bpm");
            for (int i = 0; i < rows.Count; i++)
            {
                var row = FixtureReplayTests.Obj(rows[i]);
                Assert.That(FixtureReplayTests.Number(row, "t"), Is.EqualTo(beats[i].Beat * secondsPerBeat).Within(1e-4), "scratch " + i);
                Assert.That(FixtureReplayTests.Integer(row, "lane"), Is.EqualTo(beats[i].Lane));
            }
        }
    }
}
