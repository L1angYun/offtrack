using System;
using System.IO;
using System.Text;
using Offtrack.Runtime;
using Offtrack.Runtime.Presentation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Offtrack.Editor
{
    /// <summary>真实编辑器键盘验收的只读观察器，不向游戏注入输入。</summary>
    [InitializeOnLoad]
    public static class M1KeyboardAcceptance
    {
        private const string SessionKey = "Offtrack.M1.KeyboardAcceptance";
        private const string LogPath = "Logs/M1-keyboard-session.log";
        private static string lastSnapshot;
        private static bool focused;

        static M1KeyboardAcceptance()
        {
            EditorApplication.update += Observe;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
        }

        [MenuItem("Offtrack/Start Keyboard Acceptance Session")]
        public static void Begin()
        {
            Directory.CreateDirectory("Logs");
            lastSnapshot = null;
            focused = false;
            File.WriteAllText(LogPath, "M1 编辑器键盘验收：观察真实 GameFlow；不注入输入。\n", new UTF8Encoding(false));
            SessionState.SetBool(SessionKey, true);
            EditorSceneManager.OpenScene("Assets/Scenes/Main.unity");
            EditorApplication.isPlaying = true;
        }

        [MenuItem("Offtrack/Stop Keyboard Acceptance Observer")]
        public static void Stop()
        {
            SessionState.SetBool(SessionKey, false);
            lastSnapshot = null;
            focused = false;
        }

        private static void OnPlayModeChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.ExitingPlayMode) Stop();
        }

        private static void Observe()
        {
            if (!SessionState.GetBool(SessionKey, false) || !EditorApplication.isPlaying || EditorApplication.isCompiling) return;
            var flow = UnityEngine.Object.FindAnyObjectByType<GameFlow>();
            if (flow == null || flow.Simulation == null) return;
            if (!focused)
            {
                var gameViewType = typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView");
                var gameView = EditorWindow.GetWindow(gameViewType);
                gameView.maximized = true;
                gameView.Focus();
                focused = true;
            }
            var sim = flow.Simulation;
            var keyboard = Keyboard.current;
            var snapshot = $"state={sim.State} lane={sim.Lane} jumps={sim.Jumps} lives={sim.Lives} airborne={sim.Jump != null}" +
                $" paused={flow.IsPaused} appFocused={Application.isFocused} window={EditorWindow.focusedWindow?.GetType().Name}" +
                $" keyboardEnabled={keyboard?.enabled} space={keyboard?.spaceKey.isPressed} a={keyboard?.aKey.isPressed} d={keyboard?.dKey.isPressed}";
            if (snapshot == lastSnapshot) return;
            lastSnapshot = snapshot;
            File.AppendAllText(LogPath, $"{DateTime.Now:HH:mm:ss.fff} time={sim.Time:F6} {snapshot} font={FontProvider.SelectedName}\n", new UTF8Encoding(false));
        }
    }
}
