using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

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

        private void Awake()
        {
            StandaloneWindow.ApplyJourneyWindowOnce(config.Cutscenes);
            AddCredits();
        }

        public Text Credits { get; private set; }

        // Bottom left, under the start button and clear of the half record, in the button's font.
        private void AddCredits()
        {
            var settings = config.Cutscenes;
            if (string.IsNullOrEmpty(settings.TitleCredits) || start == null) return;
            var source = start.GetComponentInChildren<Text>(true);
            var node = new GameObject("Credits", typeof(RectTransform), typeof(Text));
            var rect = node.GetComponent<RectTransform>();
            rect.SetParent(start.parent, false);
            rect.anchorMin = rect.anchorMax = new Vector2(.5f, .5f);
            rect.pivot = Vector2.zero;
            rect.anchoredPosition = settings.TitleCreditsPosition;
            rect.sizeDelta = new Vector2(520, settings.TitleCreditsFontSize * 5);
            Credits = node.GetComponent<Text>();
            Credits.font = source != null ? source.font : Presentation.FontProvider.Get();
            if (source != null) Credits.material = source.material;
            Credits.fontSize = settings.TitleCreditsFontSize;
            Credits.lineSpacing = 1.2f;
            Credits.color = Presentation.Palette.Bone40;
            Credits.alignment = TextAnchor.LowerLeft;
            Credits.horizontalOverflow = HorizontalWrapMode.Overflow;
            Credits.verticalOverflow = VerticalWrapMode.Overflow;
            Credits.raycastTarget = false;
            Credits.text = settings.TitleCredits;
        }

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
