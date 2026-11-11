using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using Offtrack.Runtime;
using Offtrack.Runtime.Presentation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Offtrack.Editor
{
    /// <summary>
    /// Visible-editor acceptance observer. Only reads the running journey and real Input System
    /// devices; it never dispatches input, invokes gameplay actions or changes game clocks.
    /// </summary>
    [InitializeOnLoad]
    public static class M2KeyboardAcceptance
    {
        private const string SessionKey = "Offtrack.M2.KeyboardAcceptance";
        private const string LogPath = "Logs/M2-keyboard-session.log";
        private const string LivePath = "Logs/M2-live-state.json";
        private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false);
        private static readonly BindingFlags Members = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
        private static readonly Key[] ObservedKeys = { Key.Space, Key.Enter, Key.NumpadEnter, Key.LeftArrow, Key.RightArrow, Key.A, Key.D, Key.R };
        private static EditorWindow gameView;
        private static string lastSnapshot;
        private static string lastScene;
        private static string lastEdges = string.Empty;
        private static int lastEdgeFrame = -1;
        private static double nextWrite;
        private static double fitAt;
        private static Rect lastWindowRect;
        private static Vector2Int lastRenderSize;
        private static bool focused;
        private static string lastObserverError;

        static M2KeyboardAcceptance()
        {
            EditorApplication.update += Observe;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
            InputSystem.onAfterUpdate += ObserveInputEdges;
        }

        [MenuItem("Offtrack/Start M2 Keyboard Acceptance")]
        public static void Begin()
        {
            Directory.CreateDirectory("Logs");
            File.WriteAllText(LogPath, "M2 编辑器键鼠验收：只读观察真实场景 / GameFlow / Input System；不注入输入、不调用游戏动作、不修改自动时钟。\n", Utf8);
            lastSnapshot = null;
            lastScene = null;
            lastEdges = string.Empty;
            lastEdgeFrame = -1;
            lastObserverError = null;
            focused = false;
            nextWrite = 0;
            SessionState.SetBool(SessionKey, true);
            EditorSceneManager.OpenScene("Assets/Scenes/CutsceneA.unity");
            FocusGameView();
            EditorApplication.isPlaying = true;
        }

        [MenuItem("Offtrack/Stop M2 Keyboard Acceptance Observer")]
        public static void Stop()
        {
            SessionState.SetBool(SessionKey, false);
            lastSnapshot = null;
            focused = false;
        }

        [MenuItem("Offtrack/End M2 Keyboard Acceptance Play Session")]
        public static void EndSession()
        {
            Stop();
            EditorApplication.isPlaying = false;
        }

        private static void OnPlayModeChanged(PlayModeStateChange state)
        {
            if (!SessionState.GetBool(SessionKey, false)) return;
            if (state == PlayModeStateChange.EnteredPlayMode)
            {
                focused = false;
                fitAt = EditorApplication.timeSinceStartup + 0.4;
            }
            else if (state == PlayModeStateChange.ExitingPlayMode)
            {
                Append("observer stopped: ExitingPlayMode");
                Stop();
            }
        }

        private static bool IsObserving => SessionState.GetBool(SessionKey, false)
            && EditorApplication.isPlaying && !EditorApplication.isCompiling;

        private static void FocusGameView()
        {
            Type type = typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView");
            if (type == null) return;
            gameView = EditorWindow.GetWindow(type);
            gameView.maximized = true;
            gameView.Focus();
            focused = true;
            fitAt = EditorApplication.timeSinceStartup + 0.4;
        }

        private static void ObserveInputEdges()
        {
            if (!IsObserving) return;
            var parts = new List<string>();
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null)
            {
                foreach (Key key in ObservedKeys)
                {
                    if (keyboard[key].wasPressedThisFrame) parts.Add(key + ":down");
                    if (keyboard[key].wasReleasedThisFrame) parts.Add(key + ":up");
                }
            }
            Mouse mouse = Mouse.current;
            if (mouse != null)
            {
                if (mouse.leftButton.wasPressedThisFrame) parts.Add("mouseLeft:down@" + mouse.position.ReadValue());
                if (mouse.leftButton.wasReleasedThisFrame) parts.Add("mouseLeft:up@" + mouse.position.ReadValue());
            }
            if (parts.Count == 0) return;
            string edges = string.Join(" ", parts);
            if (lastEdgeFrame == Time.frameCount && lastEdges == edges) return;
            lastEdges = edges;
            lastEdgeFrame = Time.frameCount;
            Append($"input frame={Time.frameCount} update={InputState.currentUpdateType} {edges}");
        }

        private static void Observe()
        {
            if (!IsObserving) return;
            try
            {
                if (!focused || gameView == null) FocusGameView();
                string scene = SceneManager.GetActiveScene().name;
                var size = new Vector2Int(Screen.width, Screen.height);
                if (scene != lastScene || (gameView != null && gameView.position != lastWindowRect) || size != lastRenderSize)
                {
                    lastScene = scene;
                    lastWindowRect = gameView != null ? gameView.position : default;
                    lastRenderSize = size;
                    fitAt = EditorApplication.timeSinceStartup + 0.2;
                }
                if (fitAt > 0 && EditorApplication.timeSinceStartup >= fitAt)
                {
                    FitGameView();
                    fitAt = 0;
                }

                var flow = Object.FindAnyObjectByType<GameFlow>();
                var shell = Object.FindAnyObjectByType<AdShell>();
                var cutscene = Object.FindAnyObjectByType<CutsceneController>();
                var sim = flow != null ? flow.Simulation : null;
                string summary = $"scene={scene} shell={shell?.State.ToString() ?? "-"} run={sim?.State.ToString() ?? "-"}" +
                    $" lane={sim?.Lane} jumps={sim?.Jumps} lives={sim?.Lives} paused={flow?.IsPaused}" +
                    $" cutsceneEnd={cutscene?.IsEnd} automatic={flow?.AutomaticTick}/{shell?.AutomaticTick}/{cutscene?.AutomaticTick}" +
                    $" appFocused={Application.isFocused} focusedWindow={EditorWindow.focusedWindow?.GetType().Name}";
                if (summary != lastSnapshot)
                {
                    lastSnapshot = summary;
                    Append($"frame={Time.frameCount} gameTime={sim?.Time:F6} cutsceneTime={cutscene?.Time:F6} {summary}");
                }
                if (EditorApplication.timeSinceStartup < nextWrite) return;
                nextWrite = EditorApplication.timeSinceStartup + 0.1;
                WriteLiveState(scene, flow, shell, cutscene);
            }
            catch (Exception exception)
            {
                string detail = exception.GetType().Name + ": " + exception.Message;
                if (detail == lastObserverError) return;
                lastObserverError = detail;
                Append("observer metadata error: " + detail);
            }
        }

        private static void FitGameView()
        {
            if (gameView == null) return;
            object zoom = ReadMember(gameView, "m_ZoomArea");
            object viewValue = ReadMember(gameView, "viewInWindow");
            object contentValue = ReadMember(gameView, "targetInContent");
            if (zoom == null || !(viewValue is Rect view) || !(contentValue is Rect content)
                || content.width <= 0 || content.height <= 0) return;
            float scale = Mathf.Min(view.width / content.width, view.height / content.height);
            var method = zoom.GetType().GetMethod("SetTransform", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null, new[] { typeof(Vector2), typeof(Vector2) }, null);
            if (method == null) return;
            method.Invoke(zoom, new object[] { Vector2.zero, Vector2.one * scale });
            FindField(gameView.GetType(), "m_defaultScale")?.SetValue(gameView, scale);
            gameView.Repaint();
            Append($"GameView Fit: scale={scale:F4} view={view} content={content}");
        }

        private static void WriteLiveState(string scene, GameFlow flow, AdShell shell, CutsceneController cutscene)
        {
            var state = new LiveState
            {
                localTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"),
                frame = Time.frameCount,
                scene = scene,
                shellState = shell != null ? shell.State.ToString() : string.Empty,
                runState = flow != null ? flow.State.ToString() : string.Empty,
                gameTime = flow != null && flow.Simulation != null ? flow.Simulation.Time : 0,
                clockTime = flow != null && flow.Clock != null ? flow.Clock.Time : 0,
                jumps = flow != null && flow.Simulation != null ? flow.Simulation.Jumps : 0,
                lives = flow != null && flow.Simulation != null ? flow.Simulation.Lives : 0,
                lane = flow != null && flow.Simulation != null ? flow.Simulation.Lane : -1,
                paused = flow != null && flow.IsPaused,
                flowAutomaticTick = flow != null && flow.AutomaticTick,
                shellAutomaticTick = shell != null && shell.AutomaticTick,
                cutsceneAutomaticTick = cutscene != null && cutscene.AutomaticTick,
                cutsceneTime = cutscene != null ? cutscene.Time : 0,
                cutsceneEnd = cutscene != null && cutscene.IsEnd,
                skipSecondsRemaining = shell != null ? shell.Machine.SkipSecondsRemaining : -1,
                canSkip = shell != null && shell.Machine.CanSkip,
                appFocused = Application.isFocused,
                focusedWindow = EditorWindow.focusedWindow != null ? EditorWindow.focusedWindow.GetType().Name : string.Empty,
                screenWidth = Screen.width,
                screenHeight = Screen.height,
                keyboardEnabled = Keyboard.current != null && Keyboard.current.enabled,
                mouseEnabled = Mouse.current != null && Mouse.current.enabled,
                mouseScreenPosition = Mouse.current != null ? Mouse.current.position.ReadValue() : Vector2.zero,
                lastInputEdges = lastEdges,
                lastInputFrame = lastEdgeFrame,
                font = FontProvider.SelectedName,
                gameViewWindowPoints = gameView != null ? gameView.position : default,
                coordinateConvention = "Unity screen: bottom-left origin. Desktop rectangles/points: top-left origin. Desktop pixels = editor points * backingScale (uniform monitor DPI)."
            };
            if (gameView != null)
            {
                object scale = ReadMember(gameView, "backingScale");
                state.backingScale = scale is float actualScale ? actualScale : EditorGUIUtility.pixelsPerPoint;
                object parent = ReadMember(gameView, "m_Parent");
                object parentPosition = ReadMember(parent, "screenPosition");
                object target = ReadMember(gameView, "targetInParent");
                if (parentPosition is Rect parentRect && target is Rect targetRect)
                {
                    state.desktopMappingAvailable = true;
                    state.gameViewParentPoints = parentRect;
                    state.renderDesktopPoints = new Rect(parentRect.position + targetRect.position, targetRect.size);
                    state.renderDesktopPixels = new Rect(state.renderDesktopPoints.position * state.backingScale,
                        state.renderDesktopPoints.size * state.backingScale);
                }
            }
            var points = new List<ControlPoint>();
            AddScreenPoint(points, state, "gameCenter", new Vector2(Screen.width * 0.5f, Screen.height * 0.5f), true);
            if (shell != null)
            {
                Camera camera = Camera.main;
                AddControl(points, state, camera, "skip", shell.SkipButton);
                AddControl(points, state, camera, "dialogPrimary", shell.Dialog.PrimaryButton);
                AddControl(points, state, camera, "dialogSecondary", shell.Dialog.SecondaryButton);
                var hud = Object.FindAnyObjectByType<HudView>();
                if (hud != null) AddControl(points, state, camera, "playOrRetry", hud.StartButton);
                if (shell.Stage != null)
                {
                    Rect rect = shell.Stage.rect;
                    AddScreenPoint(points, state, "stageLeft", RectTransformUtility.WorldToScreenPoint(camera,
                        shell.Stage.TransformPoint(new Vector3(Mathf.Lerp(rect.xMin, rect.xMax, 0.25f), rect.center.y))), true);
                    AddScreenPoint(points, state, "stageRight", RectTransformUtility.WorldToScreenPoint(camera,
                        shell.Stage.TransformPoint(new Vector3(Mathf.Lerp(rect.xMin, rect.xMax, 0.75f), rect.center.y))), true);
                }
            }
            state.controls = points.ToArray();
            string temporary = LivePath + ".tmp";
            File.WriteAllText(temporary, JsonUtility.ToJson(state, true), Utf8);
            if (File.Exists(LivePath)) File.Replace(temporary, LivePath, null);
            else File.Move(temporary, LivePath);
        }

        private static void AddControl(List<ControlPoint> points, LiveState state, Camera camera, string name, RectTransform rect)
        {
            if (rect == null) return;
            AddScreenPoint(points, state, name,
                RectTransformUtility.WorldToScreenPoint(camera, rect.TransformPoint(rect.rect.center)), rect.gameObject.activeInHierarchy);
        }

        private static void AddScreenPoint(List<ControlPoint> points, LiveState state, string name, Vector2 screenPoint, bool visible)
        {
            var normalized = new Vector2(screenPoint.x / Mathf.Max(1, state.screenWidth),
                1 - screenPoint.y / Mathf.Max(1, state.screenHeight));
            points.Add(new ControlPoint
            {
                name = name,
                visible = visible,
                screen = screenPoint,
                desktopPoints = state.renderDesktopPoints.position + Vector2.Scale(normalized, state.renderDesktopPoints.size),
                desktopPixels = state.renderDesktopPixels.position + Vector2.Scale(normalized, state.renderDesktopPixels.size)
            });
        }

        private static object ReadMember(object target, string name)
        {
            if (target == null) return null;
            for (Type type = target.GetType(); type != null; type = type.BaseType)
            {
                PropertyInfo property = type.GetProperty(name, Members);
                if (property != null) return property.GetValue(target);
                FieldInfo field = type.GetField(name, Members);
                if (field != null) return field.GetValue(target);
            }
            return null;
        }

        private static FieldInfo FindField(Type type, string name)
        {
            for (; type != null; type = type.BaseType)
            {
                FieldInfo field = type.GetField(name, Members);
                if (field != null) return field;
            }
            return null;
        }

        private static void Append(string line) => File.AppendAllText(LogPath, $"{DateTime.Now:HH:mm:ss.fff} {line}\n", Utf8);

        [Serializable]
        private sealed class ControlPoint
        {
            public string name;
            public bool visible;
            public Vector2 screen;
            public Vector2 desktopPoints;
            public Vector2 desktopPixels;
        }

        [Serializable]
        private sealed class LiveState
        {
            public string localTime, scene, shellState, runState, focusedWindow, lastInputEdges, font, coordinateConvention;
            public int frame, jumps, lives, lane, skipSecondsRemaining, screenWidth, screenHeight, lastInputFrame;
            public double gameTime, clockTime, cutsceneTime;
            public bool paused, flowAutomaticTick, shellAutomaticTick, cutsceneAutomaticTick, cutsceneEnd, canSkip;
            public bool appFocused, keyboardEnabled, mouseEnabled, desktopMappingAvailable;
            public float backingScale;
            public Vector2 mouseScreenPosition;
            public Rect gameViewWindowPoints, gameViewParentPoints, renderDesktopPoints, renderDesktopPixels;
            public ControlPoint[] controls;
        }
    }
}
