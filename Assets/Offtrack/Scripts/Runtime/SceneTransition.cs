using System;
using Offtrack.Core;
using Offtrack.Runtime.Presentation;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Offtrack.Runtime
{
    public sealed class SceneTransition : MonoBehaviour
    {
        private enum Stage { Idle, Cover, Black, Loading, Reveal, LocalCover, LocalReveal }
        private static SceneTransition instance;
        private Stage stage;
        private AsyncOperation load;
        private Canvas overlay;
        private Image ink;
        private double elapsed;
        private int blackFrames;
        private int stageStartFrame;
        private Action resume;
        private double holdDuration;
        private double revealDuration;
        private double revealOpacity;
        private Action onCovered;
        private TransitionRules rules;

        public static bool BlocksInput => instance != null && instance.stage != Stage.Idle;
        public static float Opacity => BlocksInput ? instance.ink.color.a : 0;
        public static bool AutomaticTick { get; set; } = true;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetSession()
        {
            instance = null;
            AutomaticTick = true;
            AudioListener.volume = 1;
        }

        public static bool GoTo(string scene, TransitionRules rules, Action atBlack = null)
        {
            if (BlocksInput) return false;
            EnsureInstance();
            var operation = SceneManager.LoadSceneAsync(scene, LoadSceneMode.Single);
            if (operation == null) return false;
            operation.allowSceneActivation = false;
            instance.load = operation;
            instance.rules = rules;
            instance.BindSceneCamera();
            instance.onCovered = atBlack;
            instance.Begin(Stage.Cover, 0);
            return true;
        }

        public static bool FadeLocally(Canvas target, double coverSeconds, double revealSeconds, double finalOpacity,
            Action atBlack, Action onReveal)
        {
            if (BlocksInput) return false;
            EnsureInstance();
            instance.overlay.renderMode = RenderMode.ScreenSpaceCamera;
            instance.overlay.worldCamera = target.worldCamera;
            instance.overlay.planeDistance = target.planeDistance;
            instance.holdDuration = coverSeconds;
            instance.revealDuration = revealSeconds;
            instance.revealOpacity = finalOpacity;
            instance.onCovered = atBlack;
            instance.resume = onReveal;
            instance.Begin(Stage.LocalCover, 0);
            return true;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void EnsureInstance()
        {
            if (instance == null)
            {
                var host = new GameObject(nameof(SceneTransition));
                DontDestroyOnLoad(host);
                instance = host.AddComponent<SceneTransition>();
                instance.CreateOverlay();
            }
        }

        private void Begin(Stage next, double opacity)
        {
            stage = next;
            elapsed = 0;
            stageStartFrame = Time.frameCount;
            overlay.gameObject.SetActive(true);
            SetOpacity(opacity);
        }

        public static void Advance(double seconds)
        {
            if (instance != null) instance.Tick(seconds);
        }

        private void OnEnable() => SceneManager.sceneLoaded += OnSceneLoaded;
        private void OnDisable() => SceneManager.sceneLoaded -= OnSceneLoaded;

        private void CreateOverlay()
        {
            var layer = new GameObject("TransitionInk", typeof(RectTransform), typeof(Canvas));
            layer.transform.SetParent(transform, false);
            overlay = layer.GetComponent<Canvas>();
            overlay.renderMode = RenderMode.ScreenSpaceOverlay;
            overlay.sortingOrder = short.MaxValue - 1;
            var fill = new GameObject("Fill", typeof(RectTransform), typeof(Image));
            var rect = fill.GetComponent<RectTransform>();
            rect.SetParent(layer.transform, false);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            ink = fill.GetComponent<Image>();
            ink.raycastTarget = false;
            layer.SetActive(false);
        }

        private void SetOpacity(double opacity)
        {
            Color color = Palette.TransitionInk;
            color.a = (float)opacity;
            ink.color = color;
        }

        private void BindSceneCamera()
        {
            var camera = Camera.main;
            overlay.renderMode = camera == null ? RenderMode.ScreenSpaceOverlay : RenderMode.ScreenSpaceCamera;
            overlay.worldCamera = camera;
            overlay.planeDistance = 1;
        }

        private void Update()
        {
            if (!AutomaticTick || stageStartFrame == Time.frameCount) return;
            Tick(Time.unscaledDeltaTime);
        }

        private void Tick(double seconds)
        {
            switch (stage)
            {
                case Stage.Cover:
                    elapsed += seconds;
                    double opacity = rules.InkOpacity(elapsed, false);
                    SetOpacity(opacity);
                    AudioListener.volume = (float)(1 - opacity);
                    if (elapsed + 1e-9 >= rules.FadeSeconds)
                    {
                        SetOpacity(1);
                        stage = Stage.Black;
                        blackFrames = rules.BlackFrames;
                        InvokeCovered();
                    }
                    break;
                case Stage.Black:
                    if (--blackFrames > 0 || load.progress < 0.9f) break;
                    stage = Stage.Loading;
                    load.allowSceneActivation = true;
                    break;
                case Stage.Reveal:
                    elapsed += seconds;
                    SetOpacity(rules.InkOpacity(elapsed, true));
                    if (elapsed < rules.FadeSeconds) break;
                    stage = Stage.Idle;
                    overlay.gameObject.SetActive(false);
                    break;
                case Stage.LocalCover:
                    elapsed += seconds;
                    SetOpacity(Math.Min(1, elapsed / holdDuration));
                    if (elapsed < holdDuration) break;
                    elapsed = 0;
                    stage = Stage.LocalReveal;
                    InvokeCovered();
                    break;
                case Stage.LocalReveal:
                    elapsed += seconds;
                    SetOpacity(1 - (1 - revealOpacity) * Math.Min(1, elapsed / revealDuration));
                    if (elapsed < revealDuration) break;
                    stage = Stage.Idle;
                    overlay.gameObject.SetActive(false);
                    var callback = resume;
                    resume = null;
                    callback?.Invoke();
                    break;
            }
        }

        private void InvokeCovered()
        {
            var callback = onCovered;
            onCovered = null;
            callback?.Invoke();
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (mode != LoadSceneMode.Single) return;
            AudioListener.volume = 1;
            if (!Application.isEditor) Debug.Log($"OFFTRACK scene active: {scene.name}");
            if (stage == Stage.LocalReveal || stage == Stage.LocalCover)
            {
                resume = null;
                onCovered = null;
                stage = Stage.Idle;
                overlay.gameObject.SetActive(false);
            }
            if (stage != Stage.Loading) return;
            BindSceneCamera();
            load = null;
            stage = Stage.Reveal;
            elapsed = 0;
            stageStartFrame = Time.frameCount;
        }
    }
}
