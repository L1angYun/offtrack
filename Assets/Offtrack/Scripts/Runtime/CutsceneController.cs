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

        private void Awake() => sound?.Initialize(Story, Settings, AdShell.LastJourney?.EarnedGradeCelebration == true);
        private void OnEnable() => discardNextAutomaticSample = true;

        private void Start()
        {
            if (frame.GetComponent<RectMask2D>() == null) frame.gameObject.AddComponent<RectMask2D>();
            if (sceneCamera != null) sceneCamera.backgroundColor = Palette.Ink;
            // Allocate the command pool once; rendering later frames only reuses these objects.
            while (graphics.Count < commands.Capacity) CreateGraphic();
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
            var mouse = Mouse.current;
            var press = (mouse != null && mouse.leftButton.wasPressedThisFrame)
                || (Touchscreen.current != null && Touchscreen.current.primaryTouch.press.wasPressedThisFrame);
            if (opening)
                press |= AnyKeyPressed(keyboard) || (mouse != null && (mouse.rightButton.wasPressedThisFrame
                    || mouse.middleButton.wasPressedThisFrame || mouse.backButton.wasPressedThisFrame
                    || mouse.forwardButton.wasPressedThisFrame));
            else
                press |= keyboard != null && keyboard.spaceKey.wasPressedThisFrame;
            if (IsEnd)
            {
                if (press || (keyboard != null && (keyboard.enterKey.wasPressedThisFrame || keyboard.numpadEnterKey.wasPressedThisFrame))) RestartJourney();
                else if (AutomaticTick && !discardSample) Advance(UnityEngine.Time.unscaledDeltaTime);
                return;
            }
            // Do not consume part of the .5 s hold in the same frame as the press.
            if (press && !skipped) { Skip(); return; }
            if (AutomaticTick && !discardSample) Advance(UnityEngine.Time.unscaledDeltaTime);
        }

        private static bool AnyKeyPressed(Keyboard keyboard)
        {
            if (keyboard == null) return false;
            foreach (var key in keyboard.allKeys)
                if (key.wasPressedThisFrame) return true;
            return false;
        }

        public void Seek(double time)
        {
            if (double.IsNaN(time) || double.IsInfinity(time)) throw new ArgumentOutOfRangeException(nameof(time));
            Time = Math.Max(0, Math.Min(Duration, time));
            IsEnd = false; skipped = false; holdRemaining = 0;
            sound?.Synchronize(Time);
            RenderFrame();
        }

        public void Skip()
        {
            if (IsEnd || changingScene || skipped) return;
            Time = Duration; skipped = true; holdRemaining = Settings.EndFrameHold;
            sound?.Synchronize(Time);
            if (montage || !opening) holdRemaining = 0;
            RenderFrame();
        }

        public void Advance(double delta)
        {
            if (double.IsNaN(delta) || double.IsInfinity(delta) || delta < 0) throw new ArgumentOutOfRangeException(nameof(delta));
            if (changingScene) return;
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
            return graphic;
        }

        public void RenderFrame()
        {
            if (frame == null || IsEnd) return;
            if (opening)
            {
                CutsceneTimeline.DrawOpeningPortrait(Time, Settings.Timing, Settings.Portrait, commands, out var frameState);
                CurrentFrame = frameState;
            }
            else CutsceneTimeline.DrawPortraitRoom(Story, Time, Settings.Portrait, commands, AdShell.LastJourney);
            while (graphics.Count < commands.Count) CreateGraphic();
            for (var i = 0; i < graphics.Count; i++)
            {
                var visible = i < commands.Count;
                if (graphics[i].gameObject.activeSelf != visible) graphics[i].gameObject.SetActive(visible);
                if (!visible) continue;
                graphics[i].SetCommand(commands[i], Settings.CircleSegments, viewport: frame.sizeDelta);
            }
        }
    }
}
