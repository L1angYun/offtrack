using System;
using Offtrack.Core;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Offtrack.Runtime
{
    public static class StandaloneWindow
    {
        public static int RequestCount { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetSession() => RequestCount = 0;

#if UNITY_EDITOR
        private static Func<bool> testIsFullscreen;
        private static Action<int, int> testSetResolution;

        public static void SetTestWindowBackend(Func<bool> isFullscreen, Action<int, int> setResolution)
        {
            ResetSession();
            testIsFullscreen = isFullscreen;
            testSetResolution = setResolution;
        }
#endif

        public static void ApplyJourneyWindowOnce(CutsceneSettings settings)
        {
            if (RequestCount != 0 || settings == null || !settings.ResizeStandaloneWindow) return;
#if UNITY_EDITOR
            if (testSetResolution == null) return;
            bool fullscreen = testIsFullscreen();
#elif UNITY_STANDALONE
            bool fullscreen = Screen.fullScreenMode != FullScreenMode.Windowed;
#else
            bool fullscreen = true;
#endif
            if (fullscreen) return;
#if UNITY_EDITOR
            testSetResolution(settings.JourneyWindowWidth, settings.JourneyWindowHeight);
#elif UNITY_STANDALONE
            int requestedWidth = settings.JourneyWindowWidth;
            int requestedHeight = settings.JourneyWindowHeight;

            var display = Screen.mainWindowDisplayInfo;
            int displayWidth = display.width;
            int displayHeight = display.height;
            var workArea = display.workArea;
            string source = "mainWindowDisplayInfo.workArea";
            if (displayWidth <= 0 || displayHeight <= 0 || workArea.x < 0 || workArea.y < 0
                || workArea.width <= 0 || workArea.height <= 0
                || (long)workArea.x + workArea.width > displayWidth
                || (long)workArea.y + workArea.height > displayHeight)
            {
                var resolution = Screen.currentResolution;
                displayWidth = resolution.width;
                displayHeight = resolution.height;
                workArea = new RectInt(0, 0, displayWidth, displayHeight);
                source = "currentResolution";
            }
            // A headless or temporarily unavailable display cannot supply a safe target.
            if (displayWidth <= 0 || displayHeight <= 0) return;

            // Keep the requested size's share of the screen on high-resolution displays (e.g. 4K).
            var scaled = WindowSizing.ScaleToDisplay(requestedWidth, requestedHeight, displayHeight, settings.WindowReferenceHeight);
            if (!WindowSizing.TryFitCentered(scaled.Width, scaled.Height, displayWidth, displayHeight,
                workArea.x, workArea.y, workArea.width, workArea.height,
                settings.WindowFrameWidth, settings.WindowFrameHeight, out var target)) return;
            Debug.Log($"OFFTRACK window resize: scene={SceneManager.GetActiveScene().name} " +
                $"before={Screen.width}x{Screen.height} requested={requestedWidth}x{requestedHeight} scaled={scaled.Width}x{scaled.Height} " +
                $"display={displayWidth}x{displayHeight} " +
                $"workArea=({workArea.x},{workArea.y},{workArea.width},{workArea.height}) source={source} " +
                $"frameReserve={settings.WindowFrameWidth}x{settings.WindowFrameHeight} " +
                $"target={target.Width}x{target.Height}");
            // SetResolution applies asynchronously after this frame, and Unity centers the
            // resulting outer frame on the WHOLE display. The symmetric inset calculation
            // already accounts for that placement, so there is no second move/resize race.
            // Acceptance still measures the resulting native outer/client rectangles.
            Screen.SetResolution(target.Width, target.Height, FullScreenMode.Windowed);
#endif
            RequestCount++;
        }
    }
}
