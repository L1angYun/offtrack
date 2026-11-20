using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using Offtrack.Core;
using Offtrack.Runtime;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Offtrack.Tests.PlayMode
{
    public sealed class V3StoryTests
    {
        [UnityTest]
        public IEnumerator EverySceneHasOneListenerAndACameraBoundCanvas()
        {
            foreach (var scene in OfftrackScenes.Journey)
            {
                AudioListener.volume = .4f;
                yield return SceneManager.LoadSceneAsync(scene);
                Assert.That(Object.FindObjectsByType<AudioListener>(FindObjectsSortMode.None).Length, Is.EqualTo(1), scene);
                Assert.That(Camera.main, Is.Not.Null, scene);
                foreach (var canvas in Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None))
                {
                    Assert.That(canvas.renderMode, Is.EqualTo(RenderMode.ScreenSpaceCamera), canvas.name);
                    Assert.That(canvas.worldCamera, Is.SameAs(Camera.main), canvas.name);
                }
                Assert.That(AudioListener.volume, Is.EqualTo(1), scene);
            }
        }

        // One look at a voice's own source, bracketed by the DSP clock read on either side of it.
        private struct Heard
        {
            public double From, To, Played;
            public bool Playing;
        }

        private static Heard Listen(AudioSource voice)
        {
            var heard = new Heard { From = AudioSettings.dspTime };
            heard.Played = voice.timeSamples / (double)voice.clip.frequency;
            heard.Playing = voice.isPlaying;
            heard.To = AudioSettings.dspTime;
            return heard;
        }

        // Null when the source sat at sample zero until its reservation and then ran in step with the
        // DSP clock. The slack covers the one 1024-sample mixer buffer a playhead read may be off by.
        private static string ThudTimingError(List<Heard> looks, double reserved)
        {
            const double slack = .03;
            int confirmed = 0;
            foreach (var look in looks)
            {
                if (look.To <= reserved - slack && look.Played > 0)
                    return $"Already {look.Played:0.000}s into the clip {reserved - look.To:0.000}s before the reservation.";
                if (look.From < reserved + slack || !look.Playing || look.Played <= 0) continue;
                if (look.Played < look.From - reserved - slack || look.Played > look.To - reserved + slack)
                    return $"{look.Played:0.000}s into the clip {look.From - reserved:0.000}s after the reservation.";
                confirmed++;
            }
            return confirmed > 0 ? null : "Never heard running after the reservation.";
        }

        private static AudioSource SourceOf(CutsceneSound sound, string clip)
        {
            var source = System.Array.Find(sound.GetComponents<AudioSource>(), voice => voice.clip != null && voice.clip.name == clip);
            Assert.That(source, Is.Not.Null, clip);
            return source;
        }

        // Reservations are measured against the mixer's clock, which only runs with an audio output.
        private static IEnumerator RunningDsp()
        {
            double start = AudioSettings.dspTime;
            double deadline = Time.realtimeSinceStartupAsDouble + 2;
            while (AudioSettings.dspTime <= start && Time.realtimeSinceStartupAsDouble < deadline) yield return null;
            if (AudioSettings.dspTime <= start) Assert.Inconclusive("No running audio output.");
        }

        [UnityTest]
        public IEnumerator MontageThudUsesDspSchedulingAndSurvivesTheVisualStop()
        {
            yield return SceneManager.LoadSceneAsync(OfftrackScenes.CutsceneC);
            var cutscene = Object.FindAnyObjectByType<CutsceneController>();
            cutscene.AutomaticTick = false;
            var sound = cutscene.Sound;
            var thud = SourceOf(sound, "Stop");
            yield return RunningDsp();
            cutscene.Seek(5.3);
            double before = AudioSettings.dspTime;
            // A 0.1 s step ending 0.1 s short of the stop: aiming from either wrong end of it lands 0.1 s off.
            sound.Advance(5.3, 5.4, true);
            Assert.That(sound.ScheduledStopDspTime, Is.Not.Null);
            double scheduled = sound.ScheduledStopDspTime.Value;
            Assert.That(scheduled, Is.InRange(before + .099, AudioSettings.dspTime + .101));
            var looks = new List<Heard> { Listen(thud) };
            Assert.That(looks[0].To, Is.LessThan(scheduled - .03), "The first look comes well before the reservation.");
            sound.Advance(5.4, 5.51, true);
            Assert.That(sound.ScheduledStopDspTime, Is.EqualTo(scheduled));
            Assert.That(sound.OneShotCount(StorySound.Stop), Is.Zero, "A reserved thud must not also be fired as a one-shot at 5.5s.");
            double deadline = Time.realtimeSinceStartupAsDouble + 2;
            while (looks[looks.Count - 1].From < scheduled + .06 && Time.realtimeSinceStartupAsDouble < deadline)
            {
                yield return null;
                looks.Add(Listen(thud));
            }
            // The source itself, not the recorded reservation: silent until its DSP time, then in step with it.
            Assert.That(ThudTimingError(looks, scheduled), Is.Null);
            Assert.That(sound.StopCount(StorySound.Stop), Is.EqualTo(1), "Only the seek cut this voice; the visual stop did not.");
            // Starting at once instead of on the reservation would be some 0.1 s early here; the check must see 90 ms either way.
            Assert.That(ThudTimingError(looks, scheduled + .09), Is.Not.Null);
            Assert.That(ThudTimingError(looks, scheduled - .09), Is.Not.Null);
        }

        [UnityTest]
        public IEnumerator RealtimeMontageReservesTheThudOnceAndCutsNoVoiceAtTheStop()
        {
            yield return SceneManager.LoadSceneAsync(OfftrackScenes.CutsceneC);
            var cutscene = Object.FindAnyObjectByType<CutsceneController>();
            var sound = cutscene.Sound;
            var thud = SourceOf(sound, "Stop");
            var noise = SourceOf(sound, "ClockNoise");
            var faded = new[] { StorySound.Tick, StorySound.ClockNoise, StorySound.Whoosh };
            var thuds = new List<double>();
            sound.Cue += (time, cue) => { if (cue == StorySound.Stop) thuds.Add(time); };
            Assert.That(cutscene.AutomaticTick, Is.True, "The saved scene runs on the real clock.");
            try
            {
                yield return RunningDsp();
                var looks = new List<Heard>();
                double? reserved = null;
                int shots = 0;
                int[] cuts = null;
                // A frame longer than the 0.12 s lookahead rightly skips the reservation, so such a pass is run again.
                for (int pass = 0; pass < 3 && reserved == null; pass++)
                {
                    thuds.Clear();
                    cutscene.Seek(5.3);
                    shots = sound.OneShotCount(StorySound.Stop);
                    cuts = System.Array.ConvertAll(faded, sound.StopCount);
                    // Each seek cuts the two one-shot voices and only moves the loop, which also shows the tally is live.
                    Assert.That(cuts, Is.EqualTo(new[] { pass + 1, 0, pass + 1 }));
                    double deadline = Time.realtimeSinceStartupAsDouble + 3;
                    while (cutscene.Time < 5.53 && Time.realtimeSinceStartupAsDouble < deadline)
                    {
                        double dspBefore = AudioSettings.dspTime;
                        // One production Update: unscaled frame time in, AutomaticTick passed on as the realtime flag.
                        yield return null;
                        double time = cutscene.Time;
                        if (reserved == null && sound.ScheduledStopDspTime.HasValue)
                        {
                            reserved = sound.ScheduledStopDspTime;
                            Assert.That(time, Is.InRange(5.3799, 5.5), "The reservation belongs to the last 0.12 s before the stop.");
                            Assert.That(reserved.Value - (5.5 - time), Is.InRange(dspBefore - .001, AudioSettings.dspTime + .001),
                                "The reservation is that Update's DSP clock plus the time left to 5.5 s.");
                        }
                        else if (reserved == null)
                            Assert.That(time < 5.3801 || time >= 5.5, Is.True, $"The real clock reached {time:0.000}s without reserving the thud.");
                        if (reserved != null) looks.Add(Listen(thud));
                    }
                    Assert.That(cutscene.Time, Is.GreaterThanOrEqualTo(5.53), "The real clock never carried the cutscene past its stop.");
                }
                // Hold the picture short of its 6 s exit; the reserved thud runs on the DSP clock regardless.
                cutscene.AutomaticTick = false;
                if (reserved == null) Assert.Inconclusive("Every pass had a frame longer than the lookahead.");
                Assert.That(thuds, Is.EqualTo(new[] { 5.5 }), "The stop cue is raised once, at its authored time.");
                Assert.That(sound.ScheduledStopDspTime, Is.EqualTo(reserved), "Crossing the stop keeps the one reservation.");
                Assert.That(sound.OneShotCount(StorySound.Stop) - shots, Is.Zero, "A reserved thud must not also be fired as a one-shot.");
                for (int layer = 0; layer < faded.Length; layer++)
                {
                    Assert.That(sound.Gain(faded[layer]), Is.Zero, faded[layer].ToString());
                    Assert.That(sound.StopCount(faded[layer]) - cuts[layer], Is.Zero, faded[layer] + " was cut at the stop instead of faded.");
                }
                Assert.That(noise.isPlaying, Is.True, "The silent loop keeps running.");
                Assert.That(noise.volume, Is.Zero);
                double until = Time.realtimeSinceStartupAsDouble + 2;
                while (looks[looks.Count - 1].From < reserved.Value + .06 && Time.realtimeSinceStartupAsDouble < until)
                {
                    yield return null;
                    looks.Add(Listen(thud));
                }
                Assert.That(ThudTimingError(looks, reserved.Value), Is.Null);
                Assert.That(ThudTimingError(looks, reserved.Value + .09), Is.Not.Null, "The check must see a 90 ms early start.");
            }
            finally
            {
                if (cutscene != null) cutscene.AutomaticTick = false;
            }
        }

        [UnityTest]
        public IEnumerator ManuallySteppedMontagePlaysTheThudOnceWithoutReservingIt()
        {
            yield return SceneManager.LoadSceneAsync(OfftrackScenes.CutsceneC);
            var cutscene = Object.FindAnyObjectByType<CutsceneController>();
            cutscene.AutomaticTick = false;
            var sound = cutscene.Sound;
            cutscene.Seek(5.3);
            cutscene.Advance(.1);
            // 5.4 s is inside the lookahead where the real clock reserves; a stepped clock has no DSP time to aim at.
            Assert.That(sound.ScheduledStopDspTime, Is.Null);
            Assert.That(sound.OneShotCount(StorySound.Stop), Is.Zero);
            cutscene.Advance(.11);
            Assert.That(sound.ScheduledStopDspTime, Is.Null);
            Assert.That(sound.OneShotCount(StorySound.Stop), Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator MontageEventVoicesFadeOutAtTheStopWithoutBeingStopped()
        {
            yield return SceneManager.LoadSceneAsync(OfftrackScenes.CutsceneC);
            var cutscene = Object.FindAnyObjectByType<CutsceneController>();
            cutscene.AutomaticTick = false;
            var sound = cutscene.Sound;
            var noise = SourceOf(sound, "ClockNoise");
            yield return RunningDsp();
            // Played straight through from the start: no seek or skip has touched a voice yet.
            int ticks = sound.OneShotCount(StorySound.Tick);
            cutscene.Advance(5.43 - cutscene.Time);
            Assert.That(sound.OneShotCount(StorySound.Tick) - ticks, Is.EqualTo(1), "The last tick, at 5.42 s, is still sounding at the stop.");
            Assert.That(noise.volume, Is.EqualTo(.34f).Within(.001f));
            cutscene.Advance(.12);
            Assert.That(cutscene.Time, Is.EqualTo(5.55).Within(1e-9));
            foreach (var layer in new[] { StorySound.Tick, StorySound.ClockNoise, StorySound.Whoosh })
            {
                Assert.That(sound.Gain(layer), Is.Zero, layer.ToString());
                Assert.That(sound.StopCount(layer), Is.Zero, layer + " was cut at the stop instead of faded.");
            }
            Assert.That(noise.isPlaying, Is.True, "The silent loop keeps running.");
            Assert.That(noise.volume, Is.Zero);
            int playhead = noise.timeSamples;
            double deadline = Time.realtimeSinceStartupAsDouble + 2;
            while (noise.timeSamples == playhead && Time.realtimeSinceStartupAsDouble < deadline) yield return null;
            Assert.That(noise.timeSamples, Is.Not.EqualTo(playhead), "Its playhead still moves at zero gain.");
            cutscene.Advance(.3);
            Assert.That(sound.Gain(StorySound.Stop), Is.Zero);
            Assert.That(sound.StopCount(StorySound.Stop), Is.Zero, "The thud's tail is faded the same way.");
            // A seek is the one thing that cuts a one-shot voice, which also shows the tally is live.
            cutscene.Seek(5.4);
            foreach (var layer in new[] { StorySound.Tick, StorySound.Whoosh, StorySound.Stop })
                Assert.That(sound.StopCount(layer), Is.EqualTo(1), layer.ToString());
            Assert.That(sound.StopCount(StorySound.ClockNoise), Is.Zero, "A loop is moved by a seek, not stopped.");
            Assert.That(noise.isPlaying, Is.True);
        }

        [UnityTest]
        public IEnumerator MontageWhooshLayersTrailEachPassByThirtyFiveMilliseconds()
        {
            yield return SceneManager.LoadSceneAsync(OfftrackScenes.CutsceneC);
            var cutscene = Object.FindAnyObjectByType<CutsceneController>();
            cutscene.AutomaticTick = false;
            var layers = new List<double>();
            cutscene.Sound.Cue += (time, sound) => { if (sound == StorySound.Whoosh) layers.Add(time); };
            cutscene.Seek(1.6);
            cutscene.Advance(.2);
            Assert.That(layers, Is.EqualTo(new[] { 1.65, 1.685, 1.72 }).Within(1e-9));
            Assert.That(cutscene.Sound.OneShotCount(StorySound.Whoosh), Is.EqualTo(3));
        }

        [UnityTest]
        public IEnumerator OpeningPhoneClearsAllAmbienceAndSeekDoesNotReplayCues()
        {
            yield return SceneManager.LoadSceneAsync(OfftrackScenes.CutsceneA);
            var cutscene = Object.FindAnyObjectByType<CutsceneController>();
            cutscene.AutomaticTick = false;
            var cues = new List<StorySound>();
            cutscene.Sound.Cue += (_, sound) => cues.Add(sound);
            var phone = cutscene.Settings.Timing.PhoneAppStart;
            cutscene.Seek(phone - .01);
            Assert.That(cutscene.Sound.Gain(StorySound.RoomA), Is.Positive);
            cutscene.Advance(.02);
            foreach (var layer in new[] { StorySound.RoomA, StorySound.ClassAir, StorySound.Chalk })
                Assert.That(cutscene.Sound.Gain(layer), Is.Zero);
            cutscene.Seek(2);
            cutscene.Skip();
            CollectionAssert.AreEqual(new[] { StorySound.Phone }, cues);
        }

        [UnityTest]
        public IEnumerator ClosingCrossfadesTheRoomIntoPianoAtEightPointEightSeconds()
        {
            yield return SceneManager.LoadSceneAsync(OfftrackScenes.CutsceneB);
            var cutscene = Object.FindAnyObjectByType<CutsceneController>();
            cutscene.AutomaticTick = false;
            var audiblePiano = new List<double>();
            cutscene.Sound.Cue += (time, sound) => { if (sound == StorySound.Piano) audiblePiano.Add(time); };
            cutscene.Seek(8);
            float original = cutscene.Sound.Gain(StorySound.RoomB);
            Assert.That(original, Is.Positive);
            cutscene.Advance(.4);
            Assert.That(cutscene.Sound.Gain(StorySound.RoomB), Is.EqualTo(original / 2).Within(.002));
            Assert.That(audiblePiano, Is.Empty);
            cutscene.Advance(.40001);
            Assert.That(cutscene.Sound.Gain(StorySound.RoomB), Is.Zero);
            Assert.That(cutscene.Sound.Gain(StorySound.Piano), Is.Positive);
            Assert.That(audiblePiano, Is.EqualTo(new[] { 8.8 }));
        }

        [UnityTest]
        public IEnumerator MontageSoundsStopOnTimeAndTransitionFadesToAbsoluteSilence()
        {
            yield return SceneManager.LoadSceneAsync(OfftrackScenes.CutsceneC);
            var cutscene = Object.FindAnyObjectByType<CutsceneController>();
            cutscene.AutomaticTick = false;
            SceneTransition.AutomaticTick = false;
            try
            {
                var thuds = new List<double>();
                Assert.That(cutscene.Settings.Portrait.SecondPose, Is.EqualTo(2));
                Assert.That(cutscene.Settings.Portrait.ThirdPose, Is.EqualTo(4));
                cutscene.Sound.Cue += (time, sound) => { if (sound == StorySound.Stop) thuds.Add(time); };
                cutscene.Seek(5.49);
                cutscene.Advance(.011);
                CollectionAssert.AreEqual(new[] { 5.5 }, thuds);
                Assert.That(cutscene.Sound.Gain(StorySound.Tick), Is.InRange(0, .999));
                cutscene.Advance(.3);
                foreach (var layer in new[] { StorySound.Tick, StorySound.ClockNoise, StorySound.Whoosh, StorySound.Stop })
                    Assert.That(cutscene.Sound.Gain(layer), Is.Zero, layer.ToString());
                Assert.That(cutscene.Sound.Gain(StorySound.RoomC), Is.GreaterThan(.10f));
                cutscene.Advance(.2);
                SceneTransition.Advance(.4);
                Assert.That(AudioListener.volume, Is.Zero);
                yield return V3JourneyTests.FinishTransition(OfftrackScenes.CutsceneB);
            }
            finally { SceneTransition.AutomaticTick = true; }
        }
    }
}
