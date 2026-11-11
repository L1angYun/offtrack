using System;
using Offtrack.Core;
using Offtrack.Runtime.Presentation;
using UnityEngine;

namespace Offtrack.Runtime
{
    [CreateAssetMenu(fileName = "GameConfig", menuName = "Offtrack/Game Config")]
    public sealed class GameConfig : ScriptableObject
    {
        [Header("A · Fixed gameplay parameters")]
        [Tooltip("Number of concentric tracks. Lane 0 is the outermost track.")]
        public int LaneCount = 3;
        [Tooltip("Track occupied at the beginning of every run.")]
        public int StartLane = 1;
        [Tooltip("Needle durability at the beginning of every run.")]
        public int Lives = 4;
        [Tooltip("Seconds spent airborne, immune to scratches.")]
        public double JumpDuration = 0.22;
        [Tooltip("Seconds after landing before another jump is allowed.")]
        public double SettleDuration = 0.35;
        [Tooltip("Seconds of damage immunity after a scratch hits.")]
        public double HitInvincibility = 0.65;
        [Tooltip("Half-width, in seconds, of a scratch window before the music section switch.")]
        public double EarlyWindow = 0.115;
        [Tooltip("Half-width, in seconds, of a scratch window after the music section switch.")]
        public double LateWindow = 0.145;
        [Tooltip("A hit less than this many seconds after landing is classified as landedTooEarly.")]
        public double EarlyLandingThreshold = 0.2;

        [Header("B · Music-dependent placeholders")]
        [Tooltip("Length of the playable side, in seconds. Replace when the music is final.")]
        public double Duration = 36.0;
        [Tooltip("Tempo used to convert authored beats to seconds.")]
        public double Bpm = 112.0;
        [Tooltip("Number of beats for one clockwise record revolution.")]
        public double BeatsPerRevolution = 12.0;
        [Tooltip("Scratches before this time use EarlyWindow; subsequent scratches use LateWindow.")]
        public double WindowSwitchTime = 12.0;

        [Header("Runtime")]
        [Tooltip("Maximum automatic simulation step, in seconds. A slow frame does not catch up.")]
        public double MaximumFrameDelta = 0.04;
        [Tooltip("Scratch objects created when the scene initializes; the pool never grows during a run.")]
        public int ScratchPoolSize = 32;
        [Tooltip("Seconds subtracted from a revolution when determining when a scratch becomes visible.")]
        public double ScratchLookAheadMargin = 0.35;
        [Tooltip("Seconds after a scratch's scheduled time before returning it to the pool.")]
        public double ScratchRetireDelay = 0.36;

        [Header("Presentation")]
        [Tooltip("Geometry, layout and other adjustable presentation values.")]
        public ViewSettings View = new ViewSettings();

        [Header("M2 · Journey")]
        public ShellRules Shell = new ShellRules();
        public ShellViewSettings ShellView = new ShellViewSettings();
        public CutsceneSettings Cutscenes = new CutsceneSettings();

        public RuleConfig ToRules()
        {
            var rules = new RuleConfig
            {
                LaneCount = LaneCount,
                StartLane = StartLane,
                Lives = Lives,
                JumpDuration = JumpDuration,
                SettleDuration = SettleDuration,
                HitInvincibility = HitInvincibility,
                EarlyWindow = EarlyWindow,
                LateWindow = LateWindow,
                WindowSwitchTime = WindowSwitchTime,
                EarlyLandingThreshold = EarlyLandingThreshold,
                Duration = Duration,
                Bpm = Bpm,
                BeatsPerRevolution = BeatsPerRevolution
            };
            rules.Validate();
            ValidateRuntime();
            return rules;
        }

        public void ValidateRuntime()
        {
            if (!IsFinite(MaximumFrameDelta) || MaximumFrameDelta <= 0.0)
                throw new ArgumentOutOfRangeException(nameof(MaximumFrameDelta));
            if (ScratchPoolSize <= 0)
                throw new ArgumentOutOfRangeException(nameof(ScratchPoolSize));
            if (!IsFinite(ScratchLookAheadMargin) || ScratchLookAheadMargin < 0.0)
                throw new ArgumentOutOfRangeException(nameof(ScratchLookAheadMargin));
            if (!IsFinite(ScratchRetireDelay) || ScratchRetireDelay < 0.0)
                throw new ArgumentOutOfRangeException(nameof(ScratchRetireDelay));
            if (View == null)
                throw new ArgumentException("Presentation settings are required.", nameof(View));
        }

        private static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    }
}
