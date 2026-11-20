using UnityEngine;
using UnityEngine.InputSystem;

namespace Offtrack.Runtime
{
    public sealed class TitleController : MonoBehaviour
    {
        [SerializeField] private GameConfig config;
        [SerializeField] private RectTransform start;
        [SerializeField] private RectTransform record;

        public void Configure(GameConfig configuration, RectTransform button, RectTransform disc)
        {
            config = configuration;
            start = button;
            record = disc;
        }

        private void Awake() => StandaloneWindow.ApplyJourneyWindowOnce(config.Cutscenes);

        public bool HandlePointer(Vector2 screenPoint) => !SceneTransition.BlocksInput &&
            RectTransformUtility.RectangleContainsScreenPoint(start, screenPoint, Camera.main) &&
            SceneTransition.GoTo(OfftrackScenes.CutsceneA, config.Cutscenes.Transition);

        public bool HandleKey(Key key) => !SceneTransition.BlocksInput && (key == Key.Enter || key == Key.Space) &&
            SceneTransition.GoTo(OfftrackScenes.CutsceneA, config.Cutscenes.Transition);

        private void Update()
        {
            record.Rotate(0, 0, -config.Cutscenes.TitleDegreesPerSecond * Time.unscaledDeltaTime);
            if (SceneTransition.BlocksInput) return;
            var keyboard = Keyboard.current;
            if (keyboard != null && ((keyboard.enterKey.wasPressedThisFrame && HandleKey(Key.Enter)) ||
                (keyboard.spaceKey.wasPressedThisFrame && HandleKey(Key.Space)))) return;
            var touch = Touchscreen.current;
            if (touch != null)
                foreach (var finger in touch.touches)
                    if (finger.press.wasPressedThisFrame && HandlePointer(finger.position.ReadValue())) return;
            var mouse = Mouse.current;
            if (mouse != null && mouse.leftButton.wasPressedThisFrame) HandlePointer(mouse.position.ReadValue());
        }
    }
}
