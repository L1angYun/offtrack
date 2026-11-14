using System;
using System.Collections.Generic;
using Offtrack.Core;
using Offtrack.Runtime.Presentation;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Offtrack.Runtime
{
    public sealed class CutsceneController : MonoBehaviour
    {
        [SerializeField] private bool opening;
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
        public double Duration => opening ? Settings.OpeningDuration : Settings.ClosingDuration;
        public CutsceneFrame CurrentFrame { get; private set; }
        public RectTransform Frame => frame;
        public CutsceneSettings Settings => sourceConfig != null ? sourceConfig.Cutscenes : settings;

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

        private void OnEnable() => discardNextAutomaticSample = true;

        private void Start()
        {
            if (frame.GetComponent<RectMask2D>() == null) frame.gameObject.AddComponent<RectMask2D>();
            if (sceneCamera != null) sceneCamera.backgroundColor = Palette.Ink;
            var config = Settings;
            StandaloneWindow.Resize(config, config.WindowWidth, config.WindowHeight);
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
            if (changingScene) return;
            var keyboard = Keyboard.current;
            var press = (keyboard != null && keyboard.spaceKey.wasPressedThisFrame)
                || (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
                || (Touchscreen.current != null && Touchscreen.current.primaryTouch.press.wasPressedThisFrame);
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

        public void Seek(double time)
        {
            if (double.IsNaN(time) || double.IsInfinity(time)) throw new ArgumentOutOfRangeException(nameof(time));
            Time = Math.Max(0, Math.Min(Duration, time));
            IsEnd = false; skipped = false; holdRemaining = 0;
            RenderFrame();
        }

        public void Skip()
        {
            if (IsEnd || changingScene || skipped) return;
            Time = Duration; skipped = true; holdRemaining = Settings.EndFrameHold;
            RenderFrame();
        }

        public void Advance(double delta)
        {
            if (double.IsNaN(delta) || double.IsInfinity(delta) || delta < 0) throw new ArgumentOutOfRangeException(nameof(delta));
            if (changingScene) return;
            if (IsEnd)
            {
                // The ad on the phone becomes the ad on screen: continue into Main by itself.
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
            Time = Math.Min(Duration, Time + delta);
            RenderFrame();
            if (Time >= Duration) Finish();
        }

        public void RestartJourney()
        {
            if (!IsEnd || changingScene) return;
            changingScene = true;
            // The loop restarts at the ad (Main's boot), not at the class: the ad came back.
            SceneManager.LoadScene("Main");
        }

        private void Finish()
        {
            if (opening)
            {
                changingScene = true;
                SceneManager.LoadScene("Main");
            }
            else
            {
                IsEnd = true;
                // Hold the phone's ad close-up briefly, then continue into the ad (or on any press).
                endRemaining = Settings.ClosingContinueAfter;
            }
        }

        private CutsceneGraphic CreateGraphic()
        {
            var node = new GameObject("Primitive_" + graphics.Count.ToString("000"), typeof(RectTransform), typeof(CutsceneGraphic));
            var rect = node.GetComponent<RectTransform>();
            rect.SetParent(frame, false);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = new Vector2(1600, 900);
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
            CurrentFrame = CutsceneTimeline.Draw(opening, Time, Settings.Timing, commands);
            while (graphics.Count < commands.Count) CreateGraphic();
            for (var i = 0; i < graphics.Count; i++)
            {
                var visible = i < commands.Count;
                if (graphics[i].gameObject.activeSelf != visible) graphics[i].gameObject.SetActive(visible);
                if (!visible) continue;
                var command = commands[i];
                var scale = command.ScreenSpace ? 1 : CurrentFrame.CameraScale;
                var offsetX = command.ScreenSpace ? 0 : 800 - CurrentFrame.CameraX * scale;
                var offsetY = command.ScreenSpace ? 0 : 450 - CurrentFrame.CameraY * scale;
                var opacity = command.ScreenSpace ? 1 : CurrentFrame.SceneOpacity;
                graphics[i].SetCommand(command, Settings.CircleSegments, scale, offsetX, offsetY, opacity);
            }
        }
    }
}
