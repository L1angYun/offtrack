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

    /// <summary>Placeholder, script-generated sound. Replace the clips when the music is final.</summary>
    [Serializable]
    public sealed class ProceduralAudioSettings
    {
        public bool Enabled = true;
        [Range(0, 1)] public float MasterVolume = 0.8f;
        [Range(0, 1)] public float EffectsVolume = 0.85f;
        [Range(0, 1)] public float GrooveVolume = 0.55f;
        [Tooltip("Groove gain while the needle is in the air: the music drops when the needle lifts.")]
        [Range(0, 1)] public float AirborneGain = 0.35f;
        [Tooltip("Gain of the drum voices that the current lane does not feature.")]
        [Range(0, 1)] public float UnfeaturedLaneGain = 0.35f;
        [Range(0, 1)] public float BeatGain = 0.35f;
        [Range(0, 1)] public float JumpGain = 1f;
        [Range(0, 1)] public float RejectGain = 0.8f;
        [Range(0, 1)] public float DamageGain = 1f;
        [Tooltip("Seconds between re-anchoring the groove and the moment its first event may sound.")]
        public double AnchorLead = 0.02;
        [Tooltip("Re-anchor the DSP grid when it drifts this far from the game clock (e.g. capped slow frames).")]
        public double DriftTolerance = 0.03;
        [Tooltip("Seconds of groove scheduled ahead of the game clock on the DSP clock.")]
        public double ScheduleAhead = 0.12;
        public int VoiceCount = 16;
        public int SampleRate = 44100;

        public void Validate()
        {
            if (double.IsNaN(ScheduleAhead) || double.IsInfinity(ScheduleAhead) || ScheduleAhead <= 0)
                throw new ArgumentOutOfRangeException(nameof(ScheduleAhead));
            if (VoiceCount < 4) throw new ArgumentOutOfRangeException(nameof(VoiceCount));
            if (SampleRate < 8000) throw new ArgumentOutOfRangeException(nameof(SampleRate));
        }
    }
}
