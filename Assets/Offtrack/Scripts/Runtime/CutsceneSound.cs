using System;
using System.Collections.Generic;
using System.Linq;
using Offtrack.Core;
using UnityEngine;

namespace Offtrack.Runtime
{
    public sealed class CutsceneSound : MonoBehaviour
    {
        [SerializeField] private AudioClip[] clips;
        private readonly Dictionary<StorySound, Voice> voices = new Dictionary<StorySound, Voice>();
        private (double Time, StorySound Sound)[] cues;
        private StoryScene story;
        private CutsceneSettings settings;
        private int next;
        public double? ScheduledStopDspTime { get; private set; }
        public event Action<double, StorySound> Cue;
        public float Gain(StorySound key) => voices.TryGetValue(key, out var voice) ? voice.Volume : 0;
        // Read-only tallies. Only a seek or a skip may cut a voice; a fade leaves StopCount alone.
        public int StopCount(StorySound key) => voices.TryGetValue(key, out var voice) ? voice.Stops : 0;
        public int OneShotCount(StorySound key) => voices.TryGetValue(key, out var voice) ? voice.OneShots : 0;
        public void Configure(AudioClip[] score) => clips = score;

        public void Initialize(StoryScene scene, CutsceneSettings config)
        {
            story = scene;
            settings = config;
            cues = config.Portrait.Cues(scene, config.Timing).OrderBy(cue => cue.Time).ToArray();
            var loops = scene == StoryScene.Opening ? new[] { StorySound.RoomA, StorySound.Chalk, StorySound.ClassAir }
                : scene == StoryScene.Montage ? new[] { StorySound.RoomC, StorySound.ClockNoise } : new[] { StorySound.RoomB };
            foreach (var key in loops) Source(key, true);
            Sample(0);
            if (!Application.isEditor) Debug.Log($"OFFTRACK story audio {scene}: {string.Join(", ", loops)}");
        }

        public void Synchronize(double time)
        {
            if (cues == null) return;
            ScheduledStopDspTime = null;
            next = Array.FindIndex(cues, cue => cue.Time > time);
            if (next < 0) next = cues.Length;
            foreach (var voice in voices.Values)
            {
                if (voice.Loops && voice.HasClip) voice.MoveTo(time);
                else voice.Stop();
            }
            Sample(time);
        }

        public void Advance(double previous, double time, bool realtime)
        {
            if (cues == null) return;
            var stop = settings.Portrait.Stop;
            var mix = settings.Mix;
            if (realtime && story == StoryScene.Montage && !ScheduledStopDspTime.HasValue && time < stop && stop - time <= mix.DspLookahead)
            {
                var thud = Source(StorySound.Stop, false);
                if (thud.HasClip)
                {
                    double dspNow = AudioSettings.dspTime;
                    double dspTarget = dspNow + stop - time;
                    thud.PlayScheduled(dspTarget);
                    ScheduledStopDspTime = dspTarget;
                    if (!Application.isEditor)
                        Debug.Log(FormattableString.Invariant($"OFFTRACK story reserve {story} Stop: starts at dsp {dspTarget:0.000}s, reserved at dsp {dspNow:0.000}s, cutscene {time:0.000}s"));
                }
            }
            Sample(time);
            while (next < cues.Length && cues[next].Time <= time)
            {
                var cue = cues[next++];
                var voice = Source(cue.Sound, false);
                if (cue.Sound == StorySound.Tick)
                    voice.Pitch = Mathf.Lerp(mix.TickPitchStart, mix.TickPitchEnd, (float)CutsceneTimeline.Segment(cue.Time, settings.Portrait.AccelerationStart, settings.Portrait.AccelerationEnd));
                if (cue.Sound >= StorySound.Bell) voice.Volume = OpeningEventGain(cue.Sound, time);
                if (cue.Sound == StorySound.Step) voice.Volume = mix.Step;
                if (cue.Sound == StorySound.Paper) voice.Volume = mix.Paper;
                if (cue.Sound == StorySound.Piano) voice.Volume = mix.Piano;
                if (cue.Sound == StorySound.Phone || cue.Sound == StorySound.PhoneB) voice.Volume = mix.Phone;
                bool reserved = cue.Sound == StorySound.Stop && ScheduledStopDspTime.HasValue;
                bool fired = voice.HasClip && !reserved && cue.Time >= previous && time - cue.Time < voice.Length;
                if (fired) voice.PlayOneShot();
                Cue?.Invoke(cue.Time, cue.Sound);
                if (!Application.isEditor && cue.Sound != StorySound.Tick && cue.Sound != StorySound.Whoosh)
                    Debug.Log(FormattableString.Invariant($"OFFTRACK story cue {story} {cue.Sound} @{cue.Time:0.000}s crossed at cutscene {time:0.000}s, dsp {AudioSettings.dspTime:0.000}s, {(reserved ? "reserved" : fired ? "one-shot" : "not played")}"));
            }
        }

        private Voice Source(StorySound key, bool loop)
        {
            if (voices.TryGetValue(key, out var voice)) return voice;
            var source = gameObject.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = loop;
            source.clip = clips != null && (int)key < clips.Length ? clips[(int)key] : null;
            voice = new Voice(source);
            voices.Add(key, voice);
            if (loop && voice.HasClip) { voice.Volume = 0; voice.Play(); }
            return voice;
        }

        private void Sample(double time)
        {
            var mix = settings.Mix;
            if (story == StoryScene.Opening)
            {
                var timing = settings.Timing;
                double gain = time < timing.PhoneAppStart ? 1 : 0;
                Source(StorySound.RoomA, true).Volume = (float)(mix.RoomA * gain);
                Source(StorySound.ClassAir, true).Volume = (float)(mix.ClassAir * gain);
                Source(StorySound.Chalk, true).Volume = time >= timing.WritingStart && time < timing.WritingEnd ? (float)(mix.Chalk * gain) : 0;
                foreach (var entry in voices)
                    if (entry.Key >= StorySound.Bell) entry.Value.Volume = OpeningEventGain(entry.Key, time);
            }
            else if (story == StoryScene.Montage)
            {
                var score = settings.Portrait;
                Source(StorySound.RoomC, true).Volume = (float)score.MontageRoomGain(time);
                float eventGain = (float)score.EventGain(time);
                Source(StorySound.ClockNoise, true).Volume = mix.ClockNoise * eventGain
                    * (float)CutsceneTimeline.Segment(time, score.AccelerationStart, score.AccelerationEnd);
                Source(StorySound.Tick, false).Volume = mix.Tick * eventGain;
                Source(StorySound.Whoosh, false).Volume = mix.Whoosh * eventGain;
                Source(StorySound.Stop, false).Volume = time >= score.Stop + score.Tail - 1e-8 ? 0 : mix.Thud;
            }
            else
            {
                var score = settings.Portrait;
                float pressure = Mathf.Lerp(mix.RoomB, mix.RoomBSoft, (float)CutsceneTimeline.Segment(time, score.PaperAt, score.GradeAt));
                Source(StorySound.RoomB, true).Volume = pressure * (1 - (float)CutsceneTimeline.Segment(time, score.GradeAt, score.GradeAt + score.AtmosphereFade));
            }
        }

        private float OpeningEventGain(StorySound sound, double time)
        {
            double phone = settings.Timing.PhoneAppStart;
            if (time >= phone) return 0;
            return sound < StorySound.Pack ? settings.Mix.OpeningEvent
                : settings.Mix.OpeningExit * (1 - (float)CutsceneTimeline.Segment(time, settings.Portrait.TalkAt, phone));
        }

        // The class reaches its audio sources only through here, so the tallies miss no stop and no one-shot.
        private sealed class Voice
        {
            private readonly AudioSource source;
            public Voice(AudioSource output) => source = output;
            public int Stops { get; private set; }
            public int OneShots { get; private set; }
            public bool HasClip => source.clip != null;
            public bool Loops => source.loop;
            public float Length => source.clip.length;
            public float Volume { get => source.volume; set => source.volume = value; }
            public float Pitch { get => source.pitch; set => source.pitch = value; }
            public void Play() => source.Play();
            public void PlayScheduled(double dspTime) => source.PlayScheduled(dspTime);
            public void PlayOneShot() { OneShots++; source.PlayOneShot(source.clip); }
            public void MoveTo(double time) => source.time = (float)(time % source.clip.length);
            public void Stop() { Stops++; source.Stop(); }
        }
    }
}
