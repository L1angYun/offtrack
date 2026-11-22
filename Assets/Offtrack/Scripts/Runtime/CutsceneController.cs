using System;
using System.Collections.Generic;
using Offtrack.Core;
using Offtrack.Runtime.Presentation;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Offtrack.Runtime
{
    public sealed class CutsceneController : MonoBehaviour
    {
        [SerializeField] private bool opening;
        [SerializeField] private bool montage;
        [SerializeField] private CutsceneSound sound;
        [SerializeField] private CutsceneSettings settings = new CutsceneSettings();
        [SerializeField] private GameConfig sourceConfig;
        [SerializeField] private RectTransform frame;
        [SerializeField] private Camera sceneCamera;
        [SerializeField] private Material presentationMaterial;
        private readonly List<CutsceneCommand> commands = new List<CutsceneCommand>(160);
        private readonly List<CutsceneGraphic> graphics = new List<CutsceneGraphic>(160);
        private double holdRemaining;
        private double endRemaining;
        private bool skipped, changingScene;
        private RectTransform skipButton;
        // The opening first holds a black frame naming the skip button, then fades the room up.
        private bool hinting;
        private double hintTime;
        private double revealRemaining;
        private bool automaticTick = true;
        private bool discardNextAutomaticSample = true;

        public bool AutomaticTick
        {
            get => automaticTick;
            set
            {
                if (automaticTick == value) return;
                automaticTick = value;
                discardNextAutomaticSample = true;
            }
        }
        public bool IsOpening => opening;
        public bool IsEnd { get; private set; }
        public double Time { get; private set; }
        public StoryScene Story => opening ? StoryScene.Opening : montage ? StoryScene.Montage : StoryScene.Closing;
        public double Duration => opening ? Settings.OpeningDuration : montage ? Settings.Portrait.MontageEnd : Settings.Portrait.ClosingEnd;
        public CutsceneFrame CurrentFrame { get; private set; }
        public RectTransform Frame => frame;
        public CutsceneSettings Settings => sourceConfig != null ? sourceConfig.Cutscenes : settings;
        public CutsceneSound Sound => sound;
        public RectTransform SkipButton => skipButton;
        public bool IsShowingSkipHint => hinting;

        public void ConfigureStory(StoryScene story, GameConfig configuration, Camera camera, RectTransform referenceFrame,
            Material material, CutsceneSound track)
        {
            Configure(story == StoryScene.Opening, configuration, camera, referenceFrame, material);
            montage = story == StoryScene.Montage;
            sound = track;
        }

        public void Configure(bool isOpening, CutsceneSettings configuration, Camera camera, RectTransform referenceFrame, Material material = null)
        {
            opening = isOpening; settings = configuration; sceneCamera = camera; frame = referenceFrame;
            sourceConfig = null;
            presentationMaterial = material;
        }

        public void Configure(bool isOpening, GameConfig configuration, Camera camera, RectTransform referenceFrame, Material material = null)
        {
            Configure(isOpening, configuration.Cutscenes, camera, referenceFrame, material);
            sourceConfig = configuration;
        }

        private void Awake()
        {
            sound?.Initialize(Story, Settings, AdShell.LastJourney?.EarnedGradeCelebration == true);
            hinting = opening && Settings.Portrait.SkipHintDuration > 0;
        }
        private void OnEnable() => discardNextAutomaticSample = true;

        private void Start()
        {
            if (frame.GetComponent<RectMask2D>() == null) frame.gameObject.AddComponent<RectMask2D>();
            if (sceneCamera != null) sceneCamera.backgroundColor = Palette.Ink;
            // Allocate the command pool once; rendering later frames only reuses these objects.
            while (graphics.Count < commands.Capacity) CreateGraphic();
            CreateSkipButton();
            RenderFrame();
        }

        private void Update()
        {
            // A player's first unscaledDeltaTime includes splash/scene loading. Discard
            // that stale interval so every newly visible cutscene starts at its first frame.
            bool discardSample = discardNextAutomaticSample;
            discardNextAutomaticSample = false;
            if (changingScene || SceneTransition.BlocksInput) return;
            var keyboard = Keyboard.current;
            // Only the skip button skips: a click anywhere else, or any key, leaves the scene playing.
            bool press = SkipButtonPressed();
            if (IsEnd)
            {
                if (press || (keyboard != null && (keyboard.enterKey.wasPressedThisFrame || keyboard.numpadEnterKey.wasPressedThisFrame))) RestartJourney();
                else if (AutomaticTick && !discardSample) Advance(UnityEngine.Time.unscaledDeltaTime);
                return;
            }
            if (hinting)
            {
                if (press) EndHint();
                else if (AutomaticTick && !discardSample) AdvanceHint(UnityEngine.Time.unscaledDeltaTime);
                return;
            }
            // Do not consume part of the .5 s hold in the same frame as the press.
            if (press && !skipped) { SkipShot(); return; }
            if (AutomaticTick && !discardSample) Advance(UnityEngine.Time.unscaledDeltaTime);
        }

        private bool SkipButtonPressed()
        {
            if (skipButton == null || !skipButton.gameObject.activeInHierarchy) return false;
            var mouse = Mouse.current;
            if (mouse != null && mouse.leftButton.wasPressedThisFrame && OnSkipButton(mouse.position.ReadValue())) return true;
            var touch = Touchscreen.current;
            if (touch != null)
                foreach (var finger in touch.touches)
                    if (finger.press.wasPressedThisFrame && OnSkipButton(finger.position.ReadValue())) return true;
            return false;
        }

        public bool OnSkipButton(Vector2 screenPoint)
        {
            if (skipButton == null) return false;
            var canvas = skipButton.GetComponentInParent<Canvas>();
            var camera = canvas == null || canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
            return RectTransformUtility.RectangleContainsScreenPoint(skipButton, screenPoint, camera);
        }

        private void AdvanceHint(double delta)
        {
            hintTime += delta;
            if (hintTime >= Settings.Portrait.SkipHintDuration) EndHint();
            else RenderFrame();
        }

        private void EndHint()
        {
            if (!hinting) return;
            hinting = false;
            revealRemaining = Settings.Portrait.SkipHintReveal;
            RenderFrame();
        }

        /// <summary>
        /// The skip button: jump to the start of the next shot, so one press never throws away the
        /// whole scene. A press during the last shot ends the scene as before.
        /// </summary>
        public void SkipShot()
        {
            if (IsEnd || changingScene || skipped) return;
            if (hinting) { EndHint(); return; }
            revealRemaining = 0;
            foreach (var chapter in Settings.Portrait.Chapters(Story, Settings.Timing))
            {
                if (chapter <= Time + 1e-6 || chapter >= Duration) continue;
                Time = chapter;
                sound?.Synchronize(Time);
                RenderFrame();
                return;
            }
            Skip();
        }

        public void Seek(double time)
        {
            if (double.IsNaN(time) || double.IsInfinity(time)) throw new ArgumentOutOfRangeException(nameof(time));
            Time = Math.Max(0, Math.Min(Duration, time));
            IsEnd = false; skipped = false; holdRemaining = 0;
            hinting = false; revealRemaining = 0;
            sound?.Synchronize(Time);
            RenderFrame();
        }

        /// <summary>Jump to the end of the scene at once.</summary>
        public void Skip()
        {
            if (IsEnd || changingScene || skipped) return;
            hinting = false; revealRemaining = 0;
            Time = Duration; skipped = true; holdRemaining = Settings.EndFrameHold;
            sound?.Synchronize(Time);
            if (montage || !opening) holdRemaining = 0;
            RenderFrame();
        }

        public void Advance(double delta)
        {
            if (double.IsNaN(delta) || double.IsInfinity(delta) || delta < 0) throw new ArgumentOutOfRangeException(nameof(delta));
            if (changingScene) return;
            // Driving the timeline directly starts it at once, without the opening's hint.
            if (hinting) { hinting = false; revealRemaining = 0; }
            if (IsEnd)
            {
                endRemaining -= delta;
                if (endRemaining <= 0) RestartJourney();
                return;
            }
            if (skipped)
            {
                holdRemaining -= delta;
                if (holdRemaining <= 0) Finish();
                return;
            }
            revealRemaining = Math.Max(0, revealRemaining - delta);
            var previous = Time;
            Time = Math.Min(Duration, Time + delta);
            if (delta > 0) sound?.Advance(previous, Time, AutomaticTick);
            RenderFrame();
            if (Time >= Duration) Finish();
        }

        public void RestartJourney()
        {
            if (!IsEnd || changingScene) return;
            changingScene = true;
            SceneTransition.GoTo(OfftrackScenes.Title, Settings.Transition);
        }

        private void Finish()
        {
            if (opening || montage)
            {
                changingScene = true;
                SceneTransition.GoTo(opening ? OfftrackScenes.Main : OfftrackScenes.CutsceneB, Settings.Transition);
            }
            else
            {
                IsEnd = true;
                endRemaining = Settings.ClosingContinueAfter;
                if (endRemaining <= 0) RestartJourney();
            }
        }

        private CutsceneGraphic CreateGraphic()
        {
            var node = new GameObject("Primitive_" + graphics.Count.ToString("000"), typeof(RectTransform), typeof(CutsceneGraphic));
            var rect = node.GetComponent<RectTransform>();
            rect.SetParent(frame, false);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = frame.sizeDelta;
            var graphic = node.GetComponent<CutsceneGraphic>();
            graphic.raycastTarget = false;
            graphic.material = presentationMaterial;
            graphics.Add(graphic);
            node.SetActive(false);
            if (skipButton != null) skipButton.SetAsLastSibling();
            return graphic;
        }

        // Dark and quiet on purpose: the cutscene frame keeps its one red subject.
        private void CreateSkipButton()
        {
            if (skipButton != null || frame == null) return;
            var look = Settings;
            var node = new GameObject("SkipButton", typeof(RectTransform), typeof(FlatShape));
            skipButton = node.GetComponent<RectTransform>();
            skipButton.SetParent(frame, false);
            skipButton.anchorMin = skipButton.anchorMax = skipButton.pivot = new Vector2(1, 1);
            skipButton.anchoredPosition = look.SkipButtonOffset;
            skipButton.sizeDelta = look.SkipButtonSize;
            var fill = node.GetComponent<FlatShape>();
            fill.color = Palette.DarkRed15;
            fill.material = presentationMaterial;
            fill.raycastTarget = false;
            float edge = look.SkipButtonOutline;
            void Edge(string name, Vector2 anchor, Vector2 size)
            {
                var line = new GameObject(name, typeof(RectTransform), typeof(FlatShape));
                var rect = line.GetComponent<RectTransform>();
                rect.SetParent(skipButton, false);
                rect.anchorMin = rect.anchorMax = rect.pivot = anchor;
                rect.anchoredPosition = Vector2.zero;
                rect.sizeDelta = size;
                var shape = line.GetComponent<FlatShape>();
                shape.color = Palette.DarkRed40;
                shape.material = presentationMaterial;
                shape.raycastTarget = false;
            }
            Edge("Top", new Vector2(.5f, 1), new Vector2(look.SkipButtonSize.x, edge));
            Edge("Bottom", new Vector2(.5f, 0), new Vector2(look.SkipButtonSize.x, edge));
            Edge("Left", new Vector2(0, .5f), new Vector2(edge, look.SkipButtonSize.y));
            Edge("Right", new Vector2(1, .5f), new Vector2(edge, look.SkipButtonSize.y));
            var copy = new GameObject("SkipCopy", typeof(RectTransform), typeof(Text));
            var copyRect = copy.GetComponent<RectTransform>();
            copyRect.SetParent(skipButton, false);
            copyRect.anchorMin = Vector2.zero;
            copyRect.anchorMax = Vector2.one;
            copyRect.offsetMin = copyRect.offsetMax = Vector2.zero;
            var label = copy.GetComponent<Text>();
            label.font = FontProvider.Get();
            label.fontSize = look.SkipButtonFontSize;
            label.color = Palette.Bone60;
            label.alignment = TextAnchor.MiddleCenter;
            label.horizontalOverflow = HorizontalWrapMode.Overflow;
            label.verticalOverflow = VerticalWrapMode.Overflow;
            label.material = presentationMaterial;
            label.raycastTarget = false;
            label.text = "跳过";
            skipButton.SetAsLastSibling();
        }

        public void RenderFrame()
        {
            if (skipButton != null)
            {
                // Nothing is left to skip once the last frame is reached.
                bool live = !IsEnd && Time < Duration;
                if (skipButton.gameObject.activeSelf != live) skipButton.gameObject.SetActive(live);
            }
            if (frame == null || IsEnd) return;
            if (hinting) CutsceneTimeline.DrawSkipHint(hintTime, Settings.Portrait, commands);
            else if (opening)
            {
                CutsceneTimeline.DrawOpeningPortrait(Time, Settings.Timing, Settings.Portrait, commands, out var frameState);
                CurrentFrame = frameState;
            }
            else CutsceneTimeline.DrawPortraitRoom(Story, Time, Settings.Portrait, commands, AdShell.LastJourney);
            while (graphics.Count < commands.Count) CreateGraphic();
            // After the hint the room fades up through the named steps of each ink.
            double reveal = Settings.Portrait.SkipHintReveal;
            double opacity = revealRemaining > 0 && reveal > 0 ? CutsceneTimeline.EaseInOut(1 - revealRemaining / reveal) : 1;
            for (var i = 0; i < graphics.Count; i++)
            {
                var visible = i < commands.Count;
                if (graphics[i].gameObject.activeSelf != visible) graphics[i].gameObject.SetActive(visible);
                if (!visible) continue;
                graphics[i].SetCommand(commands[i], Settings.CircleSegments, opacity: opacity, viewport: frame.sizeDelta);
            }
        }
    }
}
