using System;
using System.IO;
using NUnit.Framework;
using Offtrack.Core;
using Offtrack.Runtime;
using UnityEditor;
using UnityEngine;

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
            Assert.That(rules.WindowSwitchTime, Is.EqualTo(15), "The hardening point is the start of the third section.");
            Assert.That(rules.EarlyLandingThreshold, Is.EqualTo(0.2));
            Assert.That(rules.Duration, Is.EqualTo(30));
            Assert.That(rules.Bpm, Is.EqualTo(128));
            Assert.That(rules.BeatsPerRevolution, Is.EqualTo(16), "One 4-bar section per revolution.");
            Assert.That(config.MaximumFrameDelta, Is.EqualTo(0.04));
        }

        // M4: the landed chart is generated from Charts/side-a.beats.json at the calibrated tempo.
        [Test]
        public void LandedChartJsonMatchesTheSideABeatsWithinOneTenThousandthSecond()
        {
            var config = AssetDatabase.LoadAssetAtPath<GameConfig>("Assets/Offtrack/Config/GameConfig.asset");
            var source = FixtureJson.Parse(File.ReadAllText(Path.Combine(FixtureReplayTests.ProjectRoot, "Assets/Offtrack/Charts/side-a.beats.json")));
            var beats = FixtureReplayTests.Array(FixtureReplayTests.Obj(source)["beats"]);
            var chartJson = FixtureJson.Parse(File.ReadAllText(Path.Combine(FixtureReplayTests.ProjectRoot, "Assets/Offtrack/Charts/side-a.json")));
            var rows = FixtureReplayTests.Array(chartJson);
            Assert.That(rows, Has.Count.EqualTo(25));
            Assert.That(rows, Has.Count.EqualTo(beats.Count));
            double secondsPerBeat = 60 / config.Bpm;
            for (int i = 0; i < rows.Count; i++)
            {
                var row = FixtureReplayTests.Obj(rows[i]);
                var beat = FixtureReplayTests.Obj(beats[i]);
                Assert.That(FixtureReplayTests.Number(row, "t"), Is.EqualTo(FixtureReplayTests.Number(beat, "beat") * secondsPerBeat).Within(1e-4), "scratch " + i);
                Assert.That(FixtureReplayTests.Integer(row, "lane"), Is.EqualTo(FixtureReplayTests.Integer(beat, "lane")));
            }
        }

        // GDD 5.2/5.4: four 16-beat sections (intro, verse, chorus, climax) with rising density,
        // every scratch on a strong (even) beat, at least two beats apart, all inside the side.
        [Test]
        public void SideAChartFollowsTheFourMusicSections()
        {
            var config = AssetDatabase.LoadAssetAtPath<GameConfig>("Assets/Offtrack/Config/GameConfig.asset");
            var source = FixtureJson.Parse(File.ReadAllText(Path.Combine(FixtureReplayTests.ProjectRoot, "Assets/Offtrack/Charts/side-a.beats.json")));
            var beats = FixtureReplayTests.Array(FixtureReplayTests.Obj(source)["beats"]);
            int sectionBeats = (int)config.BeatsPerRevolution;
            int sections = (int)Math.Round(config.Duration * config.Bpm / 60 / sectionBeats);
            Assert.That(sections, Is.EqualTo(4));
            var perSection = new int[sections];
            var perLane = new int[config.LaneCount];
            double previous = double.NegativeInfinity;
            foreach (var item in beats)
            {
                var row = FixtureReplayTests.Obj(item);
                double beat = FixtureReplayTests.Number(row, "beat");
                Assert.That(beat % 2, Is.Zero, "Scratches land on strong beats.");
                Assert.That(beat - previous, Is.GreaterThanOrEqualTo(2), "At least two beats apart.");
                Assert.That(beat * 60 / config.Bpm + config.LateWindow, Is.LessThan(config.Duration));
                perSection[(int)(beat / sectionBeats)]++;
                perLane[FixtureReplayTests.Integer(row, "lane")]++;
                previous = beat;
            }
            CollectionAssert.AreEqual(new[] { 3, 6, 8, 8 }, perSection);
            Assert.That(perLane, Has.All.GreaterThanOrEqualTo(8), "Every track is used.");
            Assert.That(2 * 60 / config.Bpm, Is.GreaterThanOrEqualTo(config.JumpDuration + config.SettleDuration),
                "Two beats always leave room for a full jump cycle.");
        }

        [Test]
        public void SideAMusicCoversTheWholeSide()
        {
            var config = AssetDatabase.LoadAssetAtPath<GameConfig>("Assets/Offtrack/Config/GameConfig.asset");
            var clip = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Offtrack/Audio/side-a.wav");
            Assert.That(clip, Is.Not.Null);
            Assert.That(clip.length, Is.GreaterThanOrEqualTo((float)config.Duration));
            Assert.That(clip.channels, Is.EqualTo(2));
            var importer = (AudioImporter)AssetImporter.GetAtPath("Assets/Offtrack/Audio/side-a.wav");
            Assert.That(importer.defaultSampleSettings.loadType, Is.EqualTo(AudioClipLoadType.DecompressOnLoad),
                "Sample-accurate start and position.");
            Assert.That(importer.defaultSampleSettings.preloadAudioData, Is.True, "Decoded with the scene, not at the needle drop.");
            Assert.That(config.MusicCalibration, Is.EqualTo(2));
            Assert.That(config.Audio.AnchorLead, Is.EqualTo(.05).Within(1e-9));
            Assert.That(config.Audio.LatencyCompensation, Is.LessThan(0), "Latency follows the audio device by default.");
        }
    }
}
