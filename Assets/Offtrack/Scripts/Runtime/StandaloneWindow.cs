using Offtrack.Core;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Offtrack.Runtime
{
    /// <summary>One sizing policy for both the landscape cutscenes and portrait main scene.</summary>
    public static class StandaloneWindow
    {
        public static void Resize(CutsceneSettings settings, int requestedWidth, int requestedHeight)
        {
#if UNITY_STANDALONE && !UNITY_EDITOR
            if (settings == null || !settings.ResizeStandaloneWindow || Screen.fullScreen) return;

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

            if (!WindowSizing.TryFitCentered(requestedWidth, requestedHeight, displayWidth, displayHeight,
                workArea.x, workArea.y, workArea.width, workArea.height,
                settings.WindowFrameWidth, settings.WindowFrameHeight, out var target)) return;
            Debug.Log($"OFFTRACK window resize: scene={SceneManager.GetActiveScene().name} " +
                $"before={Screen.width}x{Screen.height} requested={requestedWidth}x{requestedHeight} " +
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
        }
    }
}
