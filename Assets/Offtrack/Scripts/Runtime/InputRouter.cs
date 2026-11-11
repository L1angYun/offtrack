using Offtrack.Core;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Offtrack.Runtime
{
    [DisallowMultipleComponent, DefaultExecutionOrder(-200)]
    public sealed class InputRouter : MonoBehaviour
    {
        [SerializeField] private GameFlow flow;
        [SerializeField] private Camera inputCamera;
        [SerializeField] private RectTransform startButton;
        [SerializeField] private RectTransform stage;
        [SerializeField] private AdShell shell;

        public void Configure(GameFlow gameFlow, Camera camera, RectTransform playButton)
        {
            flow = gameFlow; inputCamera = camera; startButton = playButton;
        }

        public void ConfigureShell(AdShell adShell, RectTransform gameStage)
        {
            shell = adShell; stage = gameStage;
        }

        private void Update()
        {
            if (flow == null || flow.Simulation == null || flow.IsApplicationPaused) return;
            if (shell != null && !shell.isActiveAndEnabled) return;
            var touch = Touchscreen.current;
            var mouse = Mouse.current;
            bool touchPressed = false;
            // Inspect UI before stage input, but consume the entire frame after its first
            // UI action. Another finger must not operate the newly revealed screen.
            if (touch != null)
                foreach (var finger in touch.touches)
                {
                    if (!finger.press.wasPressedThisFrame) continue;
                    touchPressed = true;
                    if (shell != null && shell.HandlePointer(finger.position.ReadValue())) return;
                }
            bool mousePressed = !touchPressed && mouse != null && mouse.leftButton.wasPressedThisFrame;
            if (mousePressed && shell != null && shell.HandlePointer(mouse.position.ReadValue())) return;
            if (flow.IsPaused || (shell != null && (shell.Machine.IsModalOpen ||
                shell.State == AdShellState.Boot || shell.State == AdShellState.Leaving))) return;

            Keyboard keyboard = Keyboard.current;
            if (keyboard != null && keyboard.rKey.wasPressedThisFrame)
            {
                if (shell != null) shell.Restart(); else flow.StartRun();
                return;
            }
            if (flow.State != RunState.Playing)
            {
                if (keyboard != null && (keyboard.spaceKey.wasPressedThisFrame ||
                    keyboard.enterKey.wasPressedThisFrame || keyboard.numpadEnterKey.wasPressedThisFrame))
                {
                    if (shell != null) shell.StartPlaying(); else flow.StartRun();
                    return;
                }
                if (shell == null)
                {
                    var hud = flow.GetComponent<Presentation.HudView>();
                    var button = hud != null ? hud.StartButton : startButton;
                    if (touchPressed)
                        foreach (var finger in touch.touches)
                            if (finger.press.wasPressedThisFrame && Contains(button, finger.position.ReadValue())) { flow.StartRun(); return; }
                    if (mousePressed && Contains(button, mouse.position.ReadValue())) flow.StartRun();
                }
                return;
            }
            if (shell != null && !shell.Machine.AllowsNeedleInput) return;
            if (keyboard != null)
            {
                if (keyboard.leftArrowKey.wasPressedThisFrame || keyboard.aKey.wasPressedThisFrame) flow.Move(-1);
                if (keyboard.rightArrowKey.wasPressedThisFrame || keyboard.dKey.wasPressedThisFrame) flow.Move(1);
            }
            if (touchPressed)
                foreach (var finger in touch.touches)
                    if (finger.press.wasPressedThisFrame) MoveFromScreenPosition(finger.position.ReadValue());
            if (mousePressed) MoveFromScreenPosition(mouse.position.ReadValue());
        }

        private bool Contains(RectTransform rect, Vector2 position) => rect != null && rect.gameObject.activeInHierarchy &&
            RectTransformUtility.RectangleContainsScreenPoint(rect, position, inputCamera);

        private void MoveFromScreenPosition(Vector2 screenPosition)
        {
            if (stage == null)
            {
                flow.Move(InputRules.PointerDirection(screenPosition.x, Screen.width));
                return;
            }
            if (!Contains(stage, screenPosition)) return;
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(stage, screenPosition, inputCamera, out var local))
                flow.Move(local.x < stage.rect.center.x ? -1 : 1);
        }
    }
}
