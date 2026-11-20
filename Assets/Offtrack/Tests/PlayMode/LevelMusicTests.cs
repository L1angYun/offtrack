using System.Collections;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Offtrack.Core;
using Offtrack.Runtime;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Offtrack.Tests.PlayMode
{
    public sealed class LevelMusicTests
    {
        private GameFlow flow;
        private GameAudio audio;
        private AdShell shell;

        [UnitySetUp]
        public IEnumerator LoadMain()
        {
            Time.timeScale = 1;
            yield return SceneManager.LoadSceneAsync("Main", LoadSceneMode.Single);
            flow = Object.FindAnyObjectByType<GameFlow>();
            audio = Object.FindAnyObjectByType<GameAudio>();
            shell = Object.FindAnyObjectByType<AdShell>();
            flow.AutomaticTick = shell.AutomaticTick = false;
            flow.SetApplicationFocus(true);
            shell.Advance(flow.Config.Shell.BootDuration);
            Assert.That(shell.AcceptLottery(), Is.True);
            yield return null;
        }

        [TestCase(1, "level-1")]
        [TestCase(2, "level-2")]
        [TestCase(3, "side-a")]
        public void EachLevelUsesItsOwnMusicWithoutChangingRules(int level, string name)
        {
            flow.SelectLevel(level);
            Assert.That(audio.Music.name, Is.EqualTo(name));
            Assert.That(audio.MusicSource.clip, Is.SameAs(audio.Music));
            Assert.That(audio.Music.length, Is.GreaterThanOrEqualTo(30f));
            Assert.That(audio.MusicSource.isPlaying, Is.False);
            Assert.That(flow.Config.Duration, Is.EqualTo(30));
            Assert.That(flow.Config.Bpm, Is.EqualTo(128));
            Assert.That(flow.Config.JumpDuration, Is.EqualTo(.22));
            Assert.That(flow.Config.SettleDuration, Is.EqualTo(.35));
        }

        [Test]
        public void SwitchingAndRetryingStopsTheOldSongAndKeepsTheSameEffects()
        {
            var effects = audio.Clips.ToDictionary(pair => pair.Key, pair => pair.Value);
            Assert.That(shell.StartPlaying(), Is.True);
            Assert.That(audio.MusicScheduled, Is.True);
            flow.SelectLevel(2);
            Assert.That(audio.Music.name, Is.EqualTo("level-2"));
            Assert.That(audio.MusicScheduled, Is.False);
            Assert.That(audio.MusicSource.isPlaying, Is.False);
            flow.StartRun();
            Assert.That(audio.MusicScheduled, Is.True);
            Assert.That(flow.Move(-1).Result, Is.EqualTo("jump"));
            Assert.That(audio.LastEffect, Is.EqualTo(GameAudio.JumpClip));
            flow.ResetToIdle();
            Assert.That(audio.Music.name, Is.EqualTo("level-2"));
            Assert.That(audio.MusicScheduled, Is.False);
            flow.StartRun();
            Assert.That(audio.MusicStarts, Is.EqualTo(3));
            foreach (var effect in effects) Assert.That(audio.Clips[effect.Key], Is.SameAs(effect.Value));
            flow.SelectLevel(3);
            Assert.That(audio.Music.name, Is.EqualTo("side-a"));
            Assert.That(audio.MusicScheduled, Is.False);
        }

        [TestCase(1)]
        [TestCase(2)]
        public void LongerReplacementUsesTheOriginalVictoryTail(int level)
        {
            flow.SelectLevel(level);
            ChartBot.StartImmediately(flow);
            ChartBot.Play(flow, ChartBot.WinningPresses(flow));
            Assert.That(flow.Simulation.Result.Won, Is.True);
            flow.Advance(flow.StopDuration);
            var flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var baseline = (AudioClip)typeof(GameAudio).GetField("music", flags).GetValue(audio);
            var anchor = typeof(GameAudio).GetField("dspStart", flags);
            var gain = typeof(GameAudio).GetMethod("ApplyGain", flags);
            double tail = baseline.length - flow.Config.Duration;
            double middle = flow.Config.Duration + tail * .5;
            anchor.SetValue(audio, AudioSettings.dspTime - audio.OutputLatency - middle);
            gain.Invoke(audio, null);
            float baseVolume = flow.Config.Audio.MasterVolume * flow.Config.Audio.MusicVolume;
            Assert.That(audio.MusicTargetVolume, Is.EqualTo(baseVolume * .5f).Within(.01f));
            anchor.SetValue(audio, AudioSettings.dspTime - audio.OutputLatency - baseline.length - 1);
            gain.Invoke(audio, null);
            Assert.That(audio.MusicTargetVolume, Is.Zero, "A long song must not keep playing a new verse behind the result dialog.");
        }
    }
}
