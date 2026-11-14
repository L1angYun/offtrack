using System;
using UnityEngine;

namespace Offtrack.Runtime
{
    /// <summary>The turntable's own mechanical motions around a run (GDD: not shell animation).</summary>
    [Serializable]
    public sealed class MachineSettings
    {
        [Tooltip("Seconds of needle drop and platter spin-up before the first judged instant. Judge time stays 0 throughout.")]
        public double StartDuration = 0.6;
        [Tooltip("Seconds of needle lift, arm return and platter spin-down after any ending, win or loss alike.")]
        public double StopDuration = 0.8;

        public void Validate()
        {
            if (double.IsNaN(StartDuration) || double.IsInfinity(StartDuration) || StartDuration < 0)
                throw new ArgumentOutOfRangeException(nameof(StartDuration));
            if (double.IsNaN(StopDuration) || double.IsInfinity(StopDuration) || StopDuration < 0)
                throw new ArgumentOutOfRangeException(nameof(StopDuration));
        }
    }

    /// <summary>
    /// Side-A music and the needle's sound effects. The music is the clock (GDD 9.3); the effects
    /// are generated placeholders until recorded ones exist. No victory sound, no ambience (GDD 7.2).
    /// </summary>
    [Serializable]
    public sealed class GameAudioSettings
    {
        public bool Enabled = true;
        [Range(0, 1)] public float MasterVolume = 0.8f;
        [Range(0, 1)] public float MusicVolume = 0.9f;
        [Range(0, 1)] public float EffectsVolume = 0.85f;
        [Tooltip("Music gain while the needle is in the air: the music drops when the needle lifts.")]
        [Range(0, 1)] public float AirborneGain = 0.35f;
        [Range(0, 1)] public float JumpGain = 1f;
        [Range(0, 1)] public float RejectGain = 0.8f;
        [Range(0, 1)] public float DamageGain = 1f;
        [Tooltip("Seconds over which music gain changes (duck, restore) to avoid clicks.")]
        public float GainRampSeconds = 0.03f;
        [Tooltip("After a loss the music fades out over this many seconds once the needle starts to lift.")]
        public float LossFadeSeconds = 0.25f;
        [Tooltip("Delay between scheduling the music and its first sample, on the DSP clock.")]
        public double AnchorLead = 0.05;
        [Tooltip("Output latency subtracted from the music position; negative means one DSP buffer.")]
        public double LatencyCompensation = -1;
        [Tooltip("If the DSP clock stops advancing this long while playing, the game falls back to frame time.")]
        public double StallSeconds = 0.25;
        public int VoiceCount = 8;
        public int SampleRate = 44100;

        public void Validate()
        {
            if (double.IsNaN(AnchorLead) || double.IsInfinity(AnchorLead) || AnchorLead < 0)
                throw new ArgumentOutOfRangeException(nameof(AnchorLead));
            if (double.IsNaN(StallSeconds) || StallSeconds <= 0) throw new ArgumentOutOfRangeException(nameof(StallSeconds));
            if (VoiceCount < 2) throw new ArgumentOutOfRangeException(nameof(VoiceCount));
            if (SampleRate < 8000) throw new ArgumentOutOfRangeException(nameof(SampleRate));
        }
    }
}
