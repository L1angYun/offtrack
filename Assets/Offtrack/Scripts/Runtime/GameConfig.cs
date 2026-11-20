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
        [Tooltip("Duration of the authored side-A charts, also used for runs without level music.")]
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
        public LevelChart[] Levels = Array.Empty<LevelChart>();

        [HideInInspector] public int V3Revision;

        public void ApplyV3Migration()
        {
            if (V3Revision < 1)
            {
                var cutscenes = new CutsceneSettings();
                var shell = new ShellRules();
                Cutscenes.ClosingContinueAfter = cutscenes.ClosingContinueAfter;
                Shell.BootDuration = shell.BootDuration;
                Shell.BootHoldDuration = shell.BootHoldDuration;
                Machine.StartDuration = new MachineSettings().StartDuration;
                V3Revision = 1;
            }
            if (V3Revision < 2)
            {
                var defaults = new ShellViewSettings();
                ShellView.DialogWidth = defaults.DialogWidth;
                ShellView.DialogButtonSize = defaults.DialogButtonSize;
                ShellView.DialogTitleFontSize = defaults.DialogTitleFontSize;
                ShellView.DialogBodyFontSize = defaults.DialogBodyFontSize;
                ShellView.DialogTitleTracking = defaults.DialogTitleTracking;
                ShellView.DialogOutlineWidth = defaults.DialogOutlineWidth;
                V3Revision = 2;
            }
            if (V3Revision < 3)
            {
                ShellView.RecordScale = 1.06f;
                ShellView.RecordOrigin.x = 0;
                ShellView.StageSize.x = 752;
                ShellView.TimerRemainingPosition = new Vector2(248, -558);
                ShellView.TimerRemainingSize = new Vector2(228, 54);
                ShellView.TimerRemainingFontSize = 34;
                ShellView.LivesPosition = new Vector2(-154, -554);
                ShellView.DamageNoticePosition = new Vector2(-184, -597);
                View.ScratchSize = new Vector2(56, 6);
                View.ScratchHighlightWidth = 6;
                V3Revision = 3;
            }
            if (V3Revision < 4)
            {
                Cutscenes.Portrait = new PortraitScore();
                V3Revision = 4;
            }
            if (V3Revision < 5)
            {
                var dialog = new ShellViewSettings();
                ShellView.DialogLineHeight = dialog.DialogLineHeight;
                ShellView.DialogTitleGapLines = dialog.DialogTitleGapLines;
                ShellView.DialogButtonGapLines = dialog.DialogButtonGapLines;
                V3Revision = 5;
            }
            if (V3Revision < 6) V3Revision = 6;
            if (V3Revision < 7)
            {
                Cutscenes.Transition = new TransitionRules();
                ShellView.DamageNoticePosition = new ShellViewSettings().DamageNoticePosition;
                View.ArmPivot.x = 284;
                V3Revision = 7;
            }
            if (V3Revision < 8)
            {
                Cutscenes.Portrait.LightCuts = new PortraitScore().LightCuts;
                Cutscenes.Mix = new CutsceneMix();
                V3Revision = 8;
            }
            if (V3Revision < 9)
            {
                // Per-shot portrait cameras, the side-view montage figure and the four-cycle light
                // replace the earlier score wholesale; none of its values carry over.
                Cutscenes.Portrait = new PortraitScore();
                V3Revision = 9;
            }
        }

        [Header("V2 · Machine and sound")]
        public MachineSettings Machine = new MachineSettings();
        public GameAudioSettings Audio = new GameAudioSettings();
        [Header("Damage feedback")]
        public DamageFeedbackSettings DamageFeedback = new DamageFeedbackSettings();

        public RuleConfig ToRules(LevelChart level = null)
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
                Duration = level?.Music != null ? level.Music.length : Duration,
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
            DamageFeedback.Validate();
        }

        private static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    }
}
