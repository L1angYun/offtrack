using Offtrack.Core;
using UnityEngine;
using UnityEngine.UI;

namespace Offtrack.Runtime.Presentation
{
    // The same generated prefab hosts all three dialogs; input is dispatched once by InputRouter.
    public sealed class DialogView : MonoBehaviour
    {
        [SerializeField] private GameConfig config;
        [SerializeField] private Text title;
        [SerializeField] private Text body;
        [SerializeField] private RectTransform primaryButton;
        [SerializeField] private RectTransform secondaryButton;
        [SerializeField] private Text primaryLabel;
        [SerializeField] private Text secondaryLabel;
        private CanvasGroup card;
        private RectTransform cardRect;
        private Vector2 restPosition;
        private CanvasGroup shade;
        private float age;
        private float flashUntil;
        private FlatShape pressedFill;
        private Color restingFill;
        private Canvas sortingCanvas;
        private bool useExternalDim;
        private bool steadyShade;
        private float exitAge = -1;
        public bool CanPress => age >= config.Shell.DialogEntranceDuration;
        public float DimOpacity => shade == null ? 0 : shade.alpha * Palette.DialogDim.a / 255f;
        public RectTransform PrimaryButton => primaryButton;
        public RectTransform SecondaryButton => secondaryButton;

        public void Configure(GameConfig configuration, Text titleText, Text bodyText, RectTransform primary,
            RectTransform secondary, Text primaryCopy, Text secondaryCopy)
        {
            config = configuration; title = titleText; body = bodyText;
            primaryButton = primary; secondaryButton = secondary;
            primaryLabel = primaryCopy; secondaryLabel = secondaryCopy;
        }

        /// <param name="fragments">Fragments held so far; negative means one per level played.</param>
        public void Show(AdShellState state, int level, GameObject dimmer, bool steadyDim = false, int fragments = -1)
        {
            bool lottery = state == AdShellState.Lottery;
            bool confirm = state == AdShellState.ConfirmSkip;
            bool guide = state == AdShellState.Guide;
            bool fragment = state == AdShellState.Fragment;
            bool visible = lottery || confirm || guide || fragment || state == AdShellState.FailDialog;
            ResetPress();
            gameObject.SetActive(visible);
            if (dimmer != null) dimmer.SetActive(visible);
            if (!visible) return;
            if (card == null)
            {
                if (!TryGetComponent(out card)) card = gameObject.AddComponent<CanvasGroup>();
                cardRect = (RectTransform)transform;
                restPosition = cardRect.anchoredPosition;
            }
            if (shade == null && dimmer != null && !dimmer.TryGetComponent(out shade))
                shade = dimmer.AddComponent<CanvasGroup>();
            title.text = lottery ? "天降好运" : confirm ? "你真的忍心" : "很可惜";
            body.text = lottery ? "恭喜你被天降好运砸中了！！\n现在玩把小游戏，就能获得限免黑胶会员！！" : confirm
                ? "放弃掉获得黑胶会员的机会吗？" : string.Empty;
            primaryLabel.text = lottery ? "开一把" : confirm ? "还是留下" : "重试本关";
            secondaryLabel.text = confirm ? "忍痛放弃" : "放弃";
            title.gameObject.SetActive(!fragment);
            if (guide)
            {
                title.text = "玩法引导";
                body.text = level <= config.Levels.Length ? config.Levels[level - 1].Guide : level switch
                {
                    1 => "跳动指针，别让音乐停下来。",
                    2 => "路会断，划痕会跑。看清楚再跳。",
                    _ => "唱片会掉头。别信你刚才看的那一边。"
                };
                primaryLabel.text = "开始吧";
            }
            if (fragment)
            {
                // The copy follows the fragments actually held, so a level given up earlier shows.
                int earned = fragments > 0 ? Mathf.Min(fragments, 3) : level;
                body.text = earned == 3 ? "恭喜你集齐碎片，获得黑胶会员！"
                    : $"恭喜你获得黑胶碎片 ×1\n还需要再集齐 {3 - earned} 个碎片，就能够获得黑胶会员了。";
                primaryLabel.text = level == 3 ? "开心收下" : "前往下一关";
                if (earned <= config.Levels.Length) body.text = config.Levels[earned - 1].Fragment;
            }
            bool split = confirm || state == AdShellState.FailDialog;
            secondaryButton.gameObject.SetActive(split);
            Layout(split, !fragment);
            age = 0;
            exitAge = -1;
            steadyShade = steadyDim;
            Advance(0);
            if (steadyDim && shade != null) shade.alpha = 1;
        }

        private void Layout(bool split, bool hasTitle)
        {
            var view = config.ShellView;
            float width = view.DialogButtonSize.x;
            float padding = (view.DialogWidth - width) / 2;
            // A count never parts from its measure word, nor from the words before it.
            body.text = body.text.Replace(" 1 个", "\u00a01\u00a0个").Replace(" 2 个", "\u00a02\u00a0个");
            float bodyHeight = 0;
            if (body.text.Length != 0)
            {
                FontProvider.Apply(body);
                body.horizontalOverflow = HorizontalWrapMode.Wrap;
                body.verticalOverflow = VerticalWrapMode.Overflow;
                var settings = body.GetGenerationSettings(new Vector2(width, 0));
                var generator = new TextGenerator();
                // Lines land on whole device pixels, so one division by the font's own line height
                // can leave the advance a pixel out. Measure four real lines and correct the
                // spacing until the advance is the specified line height.
                float target = view.DialogLineHeight * body.pixelsPerUnit;
                float spacing = 1, best = 1, bestError = float.MaxValue;
                for (int pass = 0; pass < 6; pass++)
                {
                    settings.lineSpacing = spacing;
                    generator.Populate("一\n一\n一\n一", settings);
                    if (generator.lineCount < 4) break;
                    float advance = (generator.lines[0].topY - generator.lines[3].topY) / 3;
                    float error = Mathf.Abs(advance - target);
                    if (error < bestError) { bestError = error; best = spacing; }
                    if (error < .01f || advance <= 0) break;
                    spacing *= target / advance;
                }
                settings.lineSpacing = body.lineSpacing = best;
                generator.Populate(body.text, settings);
                bodyHeight = Mathf.Max(view.DialogLineHeight * generator.lineCount,
                    generator.rectExtents.height / body.pixelsPerUnit);
            }
            float titleHeight = hasTitle ? view.DialogTitleHeight : 0;
            float titleGap = hasTitle ? view.DialogTitleGapLines * view.DialogLineHeight : 0;
            float buttonHeight = view.DialogButtonSize.y;
            float height = padding * 2 + titleHeight + titleGap + bodyHeight
                + view.DialogButtonGapLines * view.DialogLineHeight + buttonHeight;
            cardRect.sizeDelta = new Vector2(view.DialogWidth, height);
            float contentTop = height / 2 - padding;
            title.rectTransform.anchoredPosition = new Vector2(0, contentTop - titleHeight / 2);
            body.rectTransform.sizeDelta = new Vector2(width, bodyHeight);
            body.rectTransform.anchoredPosition = new Vector2(0, contentTop - titleHeight - titleGap - bodyHeight / 2);
            float buttonY = -height / 2 + padding + buttonHeight / 2;
            float halfWidth = view.DialogButtonSize.x / 2;
            primaryButton.sizeDelta = split ? new Vector2(halfWidth, view.DialogButtonSize.y) : view.DialogButtonSize;
            secondaryButton.sizeDelta = new Vector2(halfWidth, view.DialogButtonSize.y);
            primaryButton.anchoredPosition = new Vector2(split ? halfWidth / 2 : 0, buttonY);
            secondaryButton.anchoredPosition = new Vector2(-halfWidth / 2, buttonY);
        }

        public void UseTransitionDim(bool value)
        {
            useExternalDim = value;
            if (value)
            {
                if (!TryGetComponent(out sortingCanvas)) sortingCanvas = gameObject.AddComponent<Canvas>();
                sortingCanvas.overrideSorting = true;
                sortingCanvas.sortingOrder = short.MaxValue;
            }
            else if (sortingCanvas != null) sortingCanvas.overrideSorting = false;
            if (shade != null) shade.alpha = value ? 0 : 1;
        }

        public void BeginExit() => exitAge = 0;

        public void Press(RectTransform button)
        {
            pressedFill = button.GetComponentInChildren<FlatShape>();
            if (pressedFill != null)
            {
                restingFill = pressedFill.color;
                pressedFill.color = button == primaryButton ? Palette.DialogPressed : Palette.Red15;
            }
            flashUntil = age + config.Shell.DialogPressDuration;
        }

        public void Advance(float seconds)
        {
            if (!gameObject.activeSelf || card == null) return;
            age += seconds;
            if (age >= flashUntil) ResetPress();
            float time = Mathf.Clamp01(age / config.Shell.DialogEntranceDuration);
            float ease = 1 - Mathf.Pow(1 - time, 3);
            if (exitAge >= 0)
            {
                exitAge += seconds;
                ease = 1 - Mathf.Clamp01(exitAge / config.Shell.DialogExitDuration);
                card.alpha = ease;
                if (shade != null) shade.alpha = ease;
                return;
            }
            card.alpha = ease;
            if (shade != null) shade.alpha = useExternalDim ? 0 : steadyShade ? 1 : ease;
            cardRect.anchoredPosition = restPosition + Vector2.down * (1 - ease) * config.Shell.DialogEntranceOffset;
        }

        private void ResetPress()
        {
            if (pressedFill == null) return;
            pressedFill.color = restingFill;
            pressedFill = null;
        }
    }
}
