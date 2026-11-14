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

        [Header("B · Calibrated to the side-A music (128 BPM, 4 x 4-bar sections)")]
        [Tooltip("Length of the playable side, in seconds: four 4-bar loops at 128 BPM.")]
        public double Duration = 30.0;
        [Tooltip("Tempo used to convert authored beats to seconds.")]
        public double Bpm = 128.0;
        [Tooltip("Number of beats for one clockwise record revolution: one 4-bar section per turn.")]
        public double BeatsPerRevolution = 16.0;
        [Tooltip("Scratches before this time use EarlyWindow; subsequent scratches use LateWindow. Start of the third section.")]
        public double WindowSwitchTime = 15.0;
        // One-time migration of the prototype placeholders (36 s, 112 BPM, 12 beats, 12 s).
        [HideInInspector] public int MusicCalibration;

        public void ApplySideACalibration()
        {
            if (MusicCalibration >= 2) return;
            var defaults = CreateInstance<GameConfig>();
            if (MusicCalibration < 1)
            {
                Duration = defaults.Duration;
                Bpm = defaults.Bpm;
                BeatsPerRevolution = defaults.BeatsPerRevolution;
                WindowSwitchTime = defaults.WindowSwitchTime;
            }
            // Revision 2: the music audio settings replace the prototype groove's leftovers.
            Audio.AnchorLead = defaults.Audio.AnchorLead;
            Audio.VoiceCount = defaults.Audio.VoiceCount;
            Audio.LatencyCompensation = defaults.Audio.LatencyCompensation;
            DestroyImmediate(defaults);
            MusicCalibration = 2;
        }

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

        [Header("V2 · Machine and sound")]
        public MachineSettings Machine = new MachineSettings();
        public GameAudioSettings Audio = new GameAudioSettings();

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
            Machine?.Validate();
            Audio?.Validate();
        }

        private static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    }
}
