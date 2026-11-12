using Offtrack.Core;
using UnityEngine;
using UnityEngine.UI;

namespace Offtrack.Runtime.Presentation
{
    // The same generated prefab hosts all three dialogs; input is dispatched once by InputRouter.
    public sealed class DialogView : MonoBehaviour
    {
        [SerializeField] private Text tagLabel;
        [SerializeField] private GameConfig config;
        [SerializeField] private Text title;
        [SerializeField] private Text body;
        [SerializeField] private RectTransform primaryButton;
        [SerializeField] private RectTransform secondaryButton;
        [SerializeField] private Text primaryLabel;
        [SerializeField] private Text secondaryLabel;
        public RectTransform PrimaryButton => primaryButton;
        public RectTransform SecondaryButton => secondaryButton;

        public void Configure(GameConfig configuration, Text tagText, Text titleText, Text bodyText, RectTransform primary,
            RectTransform secondary, Text primaryCopy, Text secondaryCopy)
        {
            config = configuration; tagLabel = tagText; title = titleText; body = bodyText;
            primaryButton = primary; secondaryButton = secondary;
            primaryLabel = primaryCopy; secondaryLabel = secondaryCopy;
        }

        public void Show(AdShellState state)
        {
            bool lottery = state == AdShellState.Lottery;
            bool confirm = state == AdShellState.ConfirmSkip;
            gameObject.SetActive(lottery || confirm || state == AdShellState.FailDialog);
            tagLabel.gameObject.SetActive(lottery);
            tagLabel.text = "天降好运";
            title.text = lottery ? "恭喜你被天降好运砸中了！！" : confirm ? "你真的忍心" : "很可惜";
            body.text = lottery ? "现在玩把小游戏，\n就能获得限免黑胶会员！！" : confirm
                ? "放弃掉获得黑胶会员的机会吗？" : "你未获得限免黑胶会员。";
            primaryLabel.text = lottery ? "开一把" : confirm ? "还是留下" : "再来一把！";
            secondaryLabel.text = "忍痛放弃";
            secondaryButton.gameObject.SetActive(confirm);
            // Static layouts switch instantly. No tint transitions, fade, hover or movement.
            var view = config.ShellView;
            var layout = lottery ? view.LotteryLayout : confirm ? view.ConfirmLayout : view.FailLayout;
            ((RectTransform)transform).sizeDelta = new Vector2(view.DialogSize.x, layout.Height);
            SetY(tagLabel.rectTransform, layout.TagY);
            SetY(title.rectTransform, layout.TitleY);
            SetY(body.rectTransform, layout.BodyY);
            SetY(secondaryButton, layout.SecondaryY);
            SetY(primaryButton, layout.PrimaryY);
        }

        private static void SetY(RectTransform rect, float y)
        {
            rect.anchoredPosition = new Vector2(rect.anchoredPosition.x, y);
        }
    }
}
