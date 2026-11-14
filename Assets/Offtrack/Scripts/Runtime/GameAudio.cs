using System;
using System.Collections.Generic;
using Offtrack.Core;
using UnityEngine;

namespace Offtrack.Runtime
{
    /// <summary>A playback position the game clock can follow (GDD 9.3).</summary>
    public interface IMusicClock
    {
        /// <summary>
        /// Seconds of music heard since judge time zero. False when no position is available
        /// (nothing scheduled, no audio device, or a stalled DSP clock); the flow then uses frame time.
        /// </summary>
        bool TryGetPosition(out double seconds);
    }

    /// <summary>
    /// Side-A music and the needle's sound effects. The music starts exactly when the needle lands
    /// and is the clock the judge follows; it pauses with the game, drops while the needle is in
    /// the air, and after a loss fades out as the needle lifts. A win lets the final loop's tail ring.
    /// Effects (jump, scrape, refused press) are generated with AudioClip.Create as placeholders.
    /// No victory sound, no ambience (GDD 7.2).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class GameAudio : MonoBehaviour, IMusicClock
    {
        public const string JumpClip = "needle_jump";
        public const string DamageClip = "needle_scrape";
        public const string RejectClip = "needle_reject";

        [SerializeField] private GameFlow flow;
        [SerializeField] private AudioClip music;

        private readonly Dictionary<string, AudioClip> clips = new Dictionary<string, AudioClip>();
        private AudioSource[] voices = Array.Empty<AudioSource>();
        private AudioSource musicSource;
        private int nextVoice;
        private GameAudioSettings settings;

        private bool scheduled;
        private bool paused;
        private bool startPending;
        private double dspStart;
        private double pausedAtDsp;
        private double lastDsp = double.NaN;
        private double lastDspChange;
        private double anchorPosition;
        private double anchorRealtime;
        private double lastPosition;
        private double latency;
        private double bufferSeconds;
        // Re-anchored once the source reports real playback, so a late start (decoding, a busy
        // frame) can never leave the judge ahead of what is heard.
        private bool anchoredToPlayback;

        public IReadOnlyDictionary<string, AudioClip> Clips => clips;
        public AudioClip Music => music;
        public AudioSource MusicSource => musicSource;
        public int VoiceCount => voices.Length;
        public bool MusicScheduled => scheduled;
        public bool MusicPaused => paused;
        public int MusicStarts { get; private set; }
        /// <summary>Seconds subtracted from the DSP position for the output ring and device.</summary>
        public double OutputLatency => latency;
        public bool AnchoredToPlayback => anchoredToPlayback;
        /// <summary>The music gain the module is ramping towards this frame.</summary>
        public float MusicTargetVolume { get; private set; }
        public int EffectsPlayed { get; private set; }
        public string LastEffect { get; private set; }

        public void Configure(GameFlow value, AudioClip track)
        {
            flow = value;
            music = track;
        }

        private void Awake()
        {
            if (flow == null) flow = GetComponent<GameFlow>();
            if (flow == null || flow.Config == null) return;
            settings = flow.Config.Audio;
            if (settings == null || !settings.Enabled) return;
            settings.Validate();
            BuildClips(settings.SampleRate);
            BuildVoices(settings.VoiceCount);
            AudioSettings.GetDSPBufferSize(out int bufferLength, out int bufferCount);
            int rate = Math.Max(1, AudioSettings.outputSampleRate);
            bufferSeconds = bufferLength / (double)rate;
            // The mixer runs a ring of buffers ahead of the speaker: compensate the whole ring.
            latency = settings.LatencyCompensation >= 0 ? settings.LatencyCompensation
                : bufferSeconds * Math.Max(1, bufferCount);
            if (music != null)
            {
                musicSource = new GameObject("Music").AddComponent<AudioSource>();
                musicSource.transform.SetParent(transform, false);
                musicSource.clip = music;
                musicSource.playOnAwake = false;
                musicSource.loop = false;
                musicSource.spatialBlend = 0;
                // Decode now (at scene load), never at the needle drop.
                if (music.loadState == AudioDataLoadState.Unloaded) music.LoadAudioData();
                flow.SetMusicClock(this);
            }
#if !UNITY_EDITOR
            Debug.Log("[OFFTRACK] Audio ready: music " + (music != null ? music.name + " " + music.length.ToString("0.0") + " s" : "none")
                + ", " + clips.Count + " generated effects.");
#endif
        }

        private void OnEnable()
        {
            if (flow == null) return;
            flow.PhaseChanged += HandlePhase;
            flow.MoveAttempted += HandleMove;
            flow.DamageTaken += HandleDamage;
        }

        private void OnDisable()
        {
            if (flow != null)
            {
                flow.PhaseChanged -= HandlePhase;
                flow.MoveAttempted -= HandleMove;
                flow.DamageTaken -= HandleDamage;
            }
            StopMusic();
        }

        private void OnDestroy()
        {
            foreach (var voice in voices) if (voice != null) voice.Stop();
            foreach (var clip in clips.Values) if (clip != null) Destroy(clip);
            clips.Clear();
        }

        private void HandlePhase(MachinePhase phase)
        {
            // A needle drop schedules the music so that its first sample is heard as the needle
            // lands; an immediate start schedules it now. Either way judge zero is that sample.
            if (phase == MachinePhase.Starting) StartMusic(flow.PreparationLength);
            else if (phase == MachinePhase.Running) { if (!scheduled) StartMusic(0); }
            else if (phase == MachinePhase.Parked) StopMusic();
        }

        private void StartMusic(double heardIn)
        {
            if (musicSource == null) return;
            musicSource.Stop();
            musicSource.timeSamples = 0;
            musicSource.pitch = 1;
            MusicTargetVolume = BaseMusicVolume;
            musicSource.volume = MusicTargetVolume;
            dspStart = AudioSettings.dspTime + Math.Max(settings.AnchorLead, heardIn - latency);
            musicSource.PlayScheduled(dspStart);
            scheduled = true;
            paused = startPending = anchoredToPlayback = false;
            lastPosition = 0;
            lastDsp = double.NaN;
            MusicStarts++;
        }

        private void StopMusic()
        {
            if (musicSource != null) musicSource.Stop();
            scheduled = paused = startPending = false;
            lastPosition = 0;
        }

        private float BaseMusicVolume => settings == null ? 0 : settings.MasterVolume * settings.MusicVolume;

        private void Update()
        {
            if (!scheduled || musicSource == null || flow == null || flow.Simulation == null) return;
            bool pausedNow = flow.IsPaused;
            double dsp = AudioSettings.dspTime;
            if (pausedNow && !paused)
            {
                pausedAtDsp = dsp;
                // A source that has not started yet is rescheduled on resume instead of paused.
                startPending = dsp < dspStart;
                if (startPending) musicSource.Stop(); else musicSource.Pause();
                paused = true;
            }
            else if (!pausedNow && paused)
            {
                if (startPending)
                {
                    dspStart = dsp + Math.Max(settings.AnchorLead, dspStart - pausedAtDsp);
                    musicSource.PlayScheduled(dspStart);
                }
                else
                {
                    musicSource.UnPause();
                    dspStart += dsp - pausedAtDsp;
                }
                paused = startPending = anchoredToPlayback = false;
                lastDsp = double.NaN;
            }
            AnchorToPlayback();
            ApplyGain();
        }

        // Once samples are actually playing, derive the start from them: the clock then follows
        // the real playback position (GDD 9.3) even if the source started later than scheduled.
        private void AnchorToPlayback()
        {
            if (anchoredToPlayback || paused || !musicSource.isPlaying || musicSource.timeSamples <= 0 || music == null) return;
            dspStart = AudioSettings.dspTime - musicSource.timeSamples / (double)music.frequency;
            anchoredToPlayback = true;
            lastDsp = double.NaN;
        }

        private void ApplyGain()
        {
            var sim = flow.Simulation;
            float target = BaseMusicVolume;
            float pitch = 1;
            if (flow.Phase == MachinePhase.Running && sim.Jump != null) target *= settings.AirborneGain;
            if ((flow.Phase == MachinePhase.Stopping || flow.Phase == MachinePhase.Stopped)
                && sim.Result != null && !sim.Result.Won)
            {
                // The platter slows while the needle is still down, then the needle lifts off the music.
                double hold = flow.StopDuration * flow.Config.View.StopHoldShare;
                double elapsed = flow.PhaseElapsed;
                pitch = Mathf.Max(.05f, 1 - (float)flow.PhaseProgress);
                float fade = elapsed <= hold ? 1 : Mathf.Clamp01(1 - (float)((elapsed - hold) / Math.Max(1e-3, settings.LossFadeSeconds)));
                target *= fade;
                if (flow.Phase == MachinePhase.Stopped) target = 0;
            }
            MusicTargetVolume = target;
            float step = settings.GainRampSeconds <= 0 ? 1 : Time.unscaledDeltaTime / settings.GainRampSeconds * BaseMusicVolume;
            musicSource.volume = Mathf.MoveTowards(musicSource.volume, target, Mathf.Max(step, 1e-4f));
            musicSource.pitch = pitch;
            if (target <= 0 && musicSource.volume <= 0 && flow.Phase == MachinePhase.Stopped) StopMusic();
        }

        public bool TryGetPosition(out double seconds)
        {
            seconds = 0;
            if (!scheduled || musicSource == null || settings == null || AudioSettings.outputSampleRate <= 0) return false;
            if (paused)
            {
                seconds = lastPosition;
                return true;
            }
            double dsp = AudioSettings.dspTime;
            double now = Time.realtimeSinceStartupAsDouble;
            // The DSP clock advances once per mixer buffer: anchor on each update and interpolate
            // with real time in between, never running more than one buffer ahead of it.
            if (dsp != lastDsp)
            {
                lastDsp = dsp;
                lastDspChange = now;
                anchorPosition = dsp - dspStart - latency;
                anchorRealtime = now;
            }
            else if (now - lastDspChange > settings.StallSeconds) return false;
            double estimate = Math.Min(anchorPosition + (now - anchorRealtime), anchorPosition + bufferSeconds * 1.5);
            lastPosition = Math.Max(lastPosition, estimate);
            seconds = Math.Max(0, lastPosition);
            return true;
        }

        private void HandleMove(int direction, MoveResult result)
        {
            if (settings == null) return;
            if (result.Result == "jump") PlayEffect(JumpClip, settings.JumpGain);
            else PlayEffect(RejectClip, settings.RejectGain);
        }

        private void HandleDamage(DamageEvent damage)
        {
            if (settings != null) PlayEffect(DamageClip, settings.DamageGain);
        }

        private void PlayEffect(string clip, float gain)
        {
            if (voices.Length == 0 || flow.IsPaused) return;
            var voice = voices[nextVoice];
            nextVoice = (nextVoice + 1) % voices.Length;
            voice.clip = clips[clip];
            voice.volume = settings.MasterVolume * settings.EffectsVolume * gain;
            voice.Play();
            EffectsPlayed++;
            LastEffect = clip;
        }

        private void BuildVoices(int count)
        {
            var holder = new GameObject("Voices");
            holder.transform.SetParent(transform, false);
            voices = new AudioSource[count];
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
        }

        private float previousNoise, highPassed;

        // High-passed noise: the needle-on-vinyl scrape.
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
