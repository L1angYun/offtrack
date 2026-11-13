using System;
using System.Collections.Generic;
using Offtrack.Core;
using UnityEngine;

namespace Offtrack.Runtime
{
    /// <summary>
    /// Approved placeholder sound (V2-2): every clip is generated at load with AudioClip.Create,
    /// nothing is imported. The groove follows the game clock and is scheduled on the DSP clock a
    /// little ahead; the game clock never reads audio time. The lane decides which drum leads, and
    /// lifting the needle drops the groove. No victory sound, no ambience (GDD 7.2).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ProceduralAudio : MonoBehaviour
    {
        public const string JumpClip = "needle_jump";
        public const string DamageClip = "needle_scrape";
        public const string RejectClip = "needle_reject";
        public const string BeatClip = "beat_tick";
        public const string KickClip = "groove_kick";
        public const string SnareClip = "groove_snare";
        public const string HatClip = "groove_hat";

        [SerializeField] private GameFlow flow;

        private readonly Dictionary<string, AudioClip> clips = new Dictionary<string, AudioClip>();
        private AudioSource[] voices = Array.Empty<AudioSource>();
        private bool[] grooveVoice = Array.Empty<bool>();
        private int[] voiceLane = Array.Empty<int>();
        private int nextVoice;
        private double scheduledUntil;
        private bool grooveActive;
        // DSP time that corresponds to judge time zero. Taken once per (re)sync, so the grid does
        // not inherit the mixer's buffer quantisation every frame.
        private double dspOrigin;
        private ProceduralAudioSettings settings;

        public IReadOnlyDictionary<string, AudioClip> Clips => clips;
        public int VoiceCount => voices.Length;
        public bool GrooveActive => grooveActive;
        public int ScheduledGrooveEvents { get; private set; }
        public int EffectsPlayed { get; private set; }
        public string LastEffect { get; private set; }
        public int CancelledGrooves { get; private set; }
        /// <summary>Gain multiplier the groove would use for a drum at the given lane emphasis now.</summary>
        public float LastGrooveGain { get; private set; }

        public void Configure(GameFlow value) => flow = value;

        private void Awake()
        {
            if (flow == null) flow = GetComponent<GameFlow>();
            if (flow == null || flow.Config == null) return;
            settings = flow.Config.Audio;
            if (settings == null || !settings.Enabled) return;
            settings.Validate();
            BuildClips(settings.SampleRate);
            BuildVoices(settings.VoiceCount);
#if !UNITY_EDITOR
            Debug.Log("[OFFTRACK] Placeholder audio ready: " + clips.Count + " generated clips, " + voices.Length + " voices.");
#endif
        }

        private void OnEnable()
        {
            if (flow == null) return;
            flow.MoveAttempted += HandleMove;
            flow.DamageTaken += HandleDamage;
        }

        private void OnDisable()
        {
            if (flow != null)
            {
                flow.MoveAttempted -= HandleMove;
                flow.DamageTaken -= HandleDamage;
            }
            CancelGroove();
        }

        private void OnDestroy()
        {
            foreach (var voice in voices) if (voice != null) voice.Stop();
            foreach (var clip in clips.Values) if (clip != null) Destroy(clip);
            clips.Clear();
        }

        private void Update()
        {
            if (settings == null || voices.Length == 0 || flow == null || flow.Simulation == null) return;
            var sim = flow.Simulation;
            bool running = flow.Phase == MachinePhase.Running && sim.State == RunState.Playing && !flow.IsPaused;
            if (!running)
            {
                // Pause, dialog, focus loss, ending or retry: drop everything already queued.
                if (grooveActive) CancelGroove();
                return;
            }
            double now = AudioSettings.dspTime;
            if (!grooveActive)
            {
                // (Re)synchronise from the current judge time; nothing catches up after a pause.
                scheduledUntil = sim.Time - 1e-9;
                dspOrigin = now + settings.AnchorLead - sim.Time;
                grooveActive = true;
            }
            else if (Math.Abs(now - dspOrigin - sim.Time) > settings.DriftTolerance + settings.AnchorLead)
                dspOrigin = now + settings.AnchorLead - sim.Time;
            ScheduleGroove(sim, sim.Time + settings.ScheduleAhead);
            ApplyLiveGains(sim);
        }

        // Gains follow the needle live: queued and sounding drums duck the moment it lifts.
        private void ApplyLiveGains(GameSimulation sim)
        {
            for (int i = 0; i < voices.Length; i++)
                if (grooveVoice[i] && voices[i] != null)
                    voices[i].volume = settings.MasterVolume * settings.GrooveVolume * GrooveGain(voiceLane[i], sim);
        }

        /// <summary>Schedules eighth-note groove events up to <paramref name="until"/> (judge seconds).</summary>
        public void ScheduleGroove(GameSimulation sim, double until)
        {
            double step = 30.0 / flow.Config.Bpm;
            long first = (long)Math.Floor(scheduledUntil / step) + 1;
            double now = AudioSettings.dspTime;
            for (long index = first; index * step <= until; index++)
            {
                double at = index * step;
                if (at > flow.Config.Duration) break;
                double dsp = Math.Max(now, dspOrigin + at);
                int inBar = (int)(index % 8);
                if (inBar == 0 || inBar == 4) Queue(KickClip, 0, sim, dsp);
                if (inBar == 2 || inBar == 6) Queue(SnareClip, 1, sim, dsp);
                Queue(HatClip, 2, sim, dsp);
                if (inBar % 2 == 0) Queue(BeatClip, -1, sim, dsp);
            }
            scheduledUntil = Math.Max(scheduledUntil, until);
        }

        /// <summary>The lane decides which drum leads; a lifted needle drops the whole groove.</summary>
        public float GrooveGain(int featuredLane, GameSimulation sim)
        {
            float gain = featuredLane < 0 ? settings.BeatGain : featuredLane == sim.Lane ? 1f : settings.UnfeaturedLaneGain;
            if (sim.Jump != null) gain *= settings.AirborneGain;
            return gain;
        }

        private void Queue(string clip, int featuredLane, GameSimulation sim, double dsp)
        {
            int slot = nextVoice;
            var voice = NextVoice(true);
            if (voice == null) return;
            voiceLane[slot] = featuredLane;
            LastGrooveGain = GrooveGain(featuredLane, sim);
            voice.clip = clips[clip];
            voice.volume = settings.MasterVolume * settings.GrooveVolume * LastGrooveGain;
            voice.PlayScheduled(dsp);
            ScheduledGrooveEvents++;
        }

        private void CancelGroove()
        {
            for (int i = 0; i < voices.Length; i++)
                if (grooveVoice[i] && voices[i] != null) voices[i].Stop();
            if (grooveActive) CancelledGrooves++;
            grooveActive = false;
        }

        private void HandleMove(int direction, MoveResult result)
        {
            if (result.Result == "jump") PlayEffect(JumpClip, settings?.JumpGain ?? 1f);
            else PlayEffect(RejectClip, settings?.RejectGain ?? 1f);
        }

        private void HandleDamage(DamageEvent damage) => PlayEffect(DamageClip, settings?.DamageGain ?? 1f);

        private void PlayEffect(string clip, float gain)
        {
            if (settings == null || voices.Length == 0 || flow.IsPaused) return;
            var voice = NextVoice(false);
            if (voice == null) return;
            voice.clip = clips[clip];
            voice.volume = settings.MasterVolume * settings.EffectsVolume * gain;
            voice.Play();
            EffectsPlayed++;
            LastEffect = clip;
        }

        private AudioSource NextVoice(bool groove)
        {
            if (voices.Length == 0) return null;
            var voice = voices[nextVoice];
            grooveVoice[nextVoice] = groove;
            nextVoice = (nextVoice + 1) % voices.Length;
            return voice;
        }

        private void BuildVoices(int count)
        {
            var holder = new GameObject("Voices");
            holder.transform.SetParent(transform, false);
            voices = new AudioSource[count];
            grooveVoice = new bool[count];
            voiceLane = new int[count];
            for (int i = 0; i < count; i++)
            {
                var source = holder.AddComponent<AudioSource>();
                source.playOnAwake = false;
                source.loop = false;
                source.spatialBlend = 0;
                voices[i] = source;
            }
        }

        private void BuildClips(int rate)
        {
            var random = new System.Random(20261112);
            Add(JumpClip, rate, .08, (t, d) => Envelope(t, d, .004) * Sweep(t, d, 340, 680) * .7f);
            Add(DamageClip, rate, .16, (t, d) => Envelope(t, d, .002) * Scrape(random) * .55f);
            Add(RejectClip, rate, .07, (t, d) => Envelope(t, d, .002) * (float)Math.Sin(2 * Math.PI * 110 * t) * .8f);
            Add(BeatClip, rate, .025, (t, d) => Envelope(t, d, .001) * (float)Math.Sin(2 * Math.PI * 1600 * t) * .35f);
            Add(KickClip, rate, .16, (t, d) => Envelope(t, d, .002) * Sweep(t, d, 120, 45) * .9f);
            Add(SnareClip, rate, .13, (t, d) => Envelope(t, d, .001) *
                ((float)(random.NextDouble() * 2 - 1) * .5f + (float)Math.Sin(2 * Math.PI * 190 * t) * .3f));
            Add(HatClip, rate, .045, (t, d) => Envelope(t, d, .001) * Scrape(random) * .3f);
        }

        private float previousNoise, highPassed;

        // High-passed noise: the needle-on-vinyl scrape and the hats.
        private float Scrape(System.Random random)
        {
            float noise = (float)(random.NextDouble() * 2 - 1);
            highPassed = .85f * (highPassed + noise - previousNoise);
            previousNoise = noise;
            return highPassed;
        }

        private static float Envelope(double t, double duration, double attack)
        {
            if (t < attack) return (float)(t / attack);
            return (float)Math.Exp(-5 * (t - attack) / Math.Max(1e-6, duration - attack));
        }

        // Phase-continuous exponential pitch sweep.
        private static float Sweep(double t, double duration, double from, double to)
        {
            double k = Math.Log(to / from) / duration;
            double phase = Math.Abs(k) < 1e-9 ? from * t : from * (Math.Exp(k * t) - 1) / k;
            return (float)Math.Sin(2 * Math.PI * phase);
        }

        private void Add(string name, int rate, double duration, Func<double, double, float> sample)
        {
            int count = Math.Max(1, (int)(rate * duration));
            var data = new float[count];
            previousNoise = highPassed = 0;
            for (int i = 0; i < count; i++) data[i] = Mathf.Clamp(sample((double)i / rate, duration), -1, 1);
            var clip = AudioClip.Create(name, count, 1, rate, false);
            clip.SetData(data, 0);
            clips[name] = clip;
        }
    }
}
