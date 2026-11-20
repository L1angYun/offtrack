using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using Offtrack.Core;
using Offtrack.Runtime;
using Offtrack.Runtime.Presentation;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Offtrack.Tests.PlayMode
{
    public sealed class DamageFeedbackTests
    {
        private GameFlow flow;
        private GameAudio audio;
        private AdShell shell;
        private DamageScreenView screen;
        private float BaseVolume => flow.Config.Audio.MasterVolume * flow.Config.Audio.MusicVolume;

        [UnitySetUp]
        public IEnumerator LoadMain()
        {
            Time.timeScale = 1;
            yield return SceneManager.LoadSceneAsync(OfftrackScenes.Main);
            flow = Object.FindAnyObjectByType<GameFlow>();
            shell = Object.FindAnyObjectByType<AdShell>();
            audio = Object.FindAnyObjectByType<GameAudio>();
            screen = flow.GetComponent<DamageScreenView>();
            flow.AutomaticTick = shell.AutomaticTick = false;
            flow.SetApplicationFocus(true);
            shell.Advance(flow.Config.Shell.BootDuration);
            Assert.That(shell.AcceptLottery(), Is.True);
            ChartBot.StartImmediately(flow);
            yield return null;
        }

        private void TakeHit()
        {
            LogAssert.Expect(LogType.Log, new Regex(@"^\[OFFTRACK\] scratch=\d+ lane=\d+ kind=(jumpedTooLate|landedTooEarly) lives=\d+$"));
            int before = flow.Simulation.DamageCount;
            for (int step = 0; step < 500 && flow.Simulation.DamageCount == before; step++) flow.Advance(ChartBot.Step);
            Assert.That(flow.Simulation.DamageCount, Is.EqualTo(before + 1));
            audio.SendMessage("Update");
        }

        private void ShieldFurtherHits() => typeof(GameSimulation).GetProperty(nameof(GameSimulation.InvincibleUntil))
            .SetValue(flow.Simulation, 100000d);

        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        public void ARealHitDucksOnlyMusicAndBoostsTheExistingDamageCue(int level)
        {
            flow.SelectLevel(level);
            ChartBot.StartImmediately(flow);
            TakeHit();
            Assert.That(audio.DamageMusicMultiplier, Is.EqualTo(.6f).Within(.001));
            Assert.That(audio.MusicTargetVolume, Is.EqualTo(BaseVolume * .6f).Within(.001));
            Assert.That(audio.LastEffect, Is.EqualTo(GameAudio.DamageClip));
            var cue = audio.GetComponentsInChildren<AudioSource>().Last(source => source.clip == audio.Clips[GameAudio.DamageClip]);
            Assert.That(cue.volume, Is.EqualTo(flow.Config.Audio.MasterVolume * flow.Config.Audio.EffectsVolume * 1.2f).Within(.001));
            Assert.That(AudioListener.volume, Is.EqualTo(1), "The damage cue must not duck the master listener.");
            Assert.That(screen.Strength, Is.EqualTo(1));
            Assert.That(screen.Vignette.EdgeAlpha, Is.GreaterThan(screen.Vignette.CenterAlpha));
            Assert.That(screen.Vignette.raycastTarget, Is.False);
            Assert.That(screen.Vignette.GetComponent<CanvasGroup>().blocksRaycasts, Is.False);
        }

        [Test]
        public void FeedbackRecoversSmoothlyAndComposesWithTheAirborneDuck()
        {
            TakeHit();
            ShieldFurtherHits();
            Assert.That(flow.Move(-1).Result, Is.EqualTo("jump"));
            audio.SendMessage("Update");
            Assert.That(audio.MusicTargetVolume, Is.EqualTo(BaseVolume * .6f * flow.Config.Audio.AirborneGain).Within(.001));
            flow.Advance(flow.Config.DamageFeedback.Duration * .6);
            Assert.That(audio.DamageMusicMultiplier, Is.GreaterThan(.6f).And.LessThan(1f));
            Assert.That(screen.Strength, Is.GreaterThan(0f).And.LessThan(1f));
            flow.Advance(flow.Config.DamageFeedback.Duration);
            audio.SendMessage("Update");
            Assert.That(audio.DamageMusicMultiplier, Is.EqualTo(1));
            Assert.That(audio.MusicTargetVolume, Is.EqualTo(BaseVolume).Within(.001));
            Assert.That(screen.Strength, Is.Zero);
        }

        [Test]
        public void PauseFreezesFeedbackAndRestartClearsItWithoutDuplicateOverlays()
        {
            TakeHit();
            double before = flow.PresentationTime;
            flow.SetApplicationFocus(false);
            flow.Advance(5);
            screen.SendMessage("Update");
            Assert.That(flow.PresentationTime, Is.EqualTo(before));
            Assert.That(audio.DamageMusicMultiplier, Is.EqualTo(.6f).Within(.001));
            Assert.That(screen.Strength, Is.Zero, "Hide the wash while the player is paused.");
            flow.SetApplicationFocus(true);
            screen.SendMessage("Update");
            Assert.That(screen.Strength, Is.EqualTo(1));
            Assert.That(shell.Restart(), Is.True);
            Assert.That(audio.DamageMusicMultiplier, Is.EqualTo(1));
            Assert.That(screen.Strength, Is.Zero);
            Assert.That(Object.FindObjectsByType<DamageVignette>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length, Is.EqualTo(1));
        }

        [Test]
        public void EnlargedHpCellsAndHollowsKeepTheOriginalBarCenter()
        {
            var frame = GameObject.Find("ReferenceFrame").transform;
            Vector2 center = Vector2.zero;
            for (int i = 0; i < flow.Config.Lives; i++)
            {
                var cell = (RectTransform)frame.Find("Life_" + i);
                Assert.That(cell.sizeDelta, Is.EqualTo(new Vector2(44, 18)));
                Assert.That(((RectTransform)cell.Find("Hollow")).sizeDelta, Is.EqualTo(cell.sizeDelta));
                Assert.That(((RectTransform)cell.Find("Hollow/Top")).sizeDelta.x, Is.EqualTo(cell.sizeDelta.x));
                Assert.That(((RectTransform)cell.Find("Hollow/Left")).sizeDelta.y, Is.EqualTo(cell.sizeDelta.y));
                center += cell.anchoredPosition;
            }
            center /= flow.Config.Lives;
            Assert.That(center, Is.EqualTo(new Vector2(-154, -554)), "Keep the original center of the HP bar.");
        }

        [UnityTest]
        public IEnumerator RenderedDamageTintAppearsThenClears()
        {
            var camera = Camera.main;
            var previousTarget = camera.targetTexture;
            float previousAspect = camera.aspect;
            var target = new RenderTexture(390, 844, 24, RenderTextureFormat.ARGB32) { antiAliasing = 1 };
            target.Create();
            var pixels = new Texture2D(390, 844, TextureFormat.RGB24, false);
            string output = Path.GetFullPath("Logs/damage-feedback-evidence");
            Directory.CreateDirectory(output);
            camera.targetTexture = target;
            camera.aspect = 390f / 844;
            try
            {
                yield return Capture("before");
                Color32 beforeEdge = pixels.GetPixel(8, 8), beforeCenter = pixels.GetPixel(195, 422);
                TakeHit();
                ShieldFurtherHits();
                yield return Capture("impact");
                Color32 impactEdge = pixels.GetPixel(8, 8), impactCenter = pixels.GetPixel(195, 422);
                int edgeShift = Redness(impactEdge) - Redness(beforeEdge);
                int centerShift = Redness(impactCenter) - Redness(beforeCenter);
                Assert.That(edgeShift, Is.GreaterThan(20), "The edge must visibly turn red in the actual camera render.");
                Assert.That(edgeShift, Is.GreaterThan(centerShift), "The center must stay clearer than the edge.");
                flow.Advance(flow.Config.DamageFeedback.Duration + .05);
                yield return Capture("recovered");
                Color32 afterEdge = pixels.GetPixel(8, 8);
                Assert.That(Math.Abs(Redness(afterEdge) - Redness(beforeEdge)), Is.LessThanOrEqualTo(2));
                LogAssert.NoUnexpectedReceived();
            }
            finally
            {
                camera.targetTexture = previousTarget;
                camera.aspect = previousAspect;
                target.Release(); Object.Destroy(target); Object.Destroy(pixels);
            }

            IEnumerator Capture(string name)
            {
                Canvas.ForceUpdateCanvases();
                yield return null; yield return null;
                var active = RenderTexture.active;
                RenderTexture.active = target;
                pixels.ReadPixels(new Rect(0, 0, 390, 844), 0, 0, false);
                pixels.Apply(); RenderTexture.active = active;
                File.WriteAllBytes(Path.Combine(output, name + ".png"), pixels.EncodeToPNG());
            }
        }

        private static int Redness(Color32 pixel) => pixel.r - pixel.g;
    }
}
