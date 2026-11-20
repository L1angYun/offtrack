using System.Linq;
using NUnit.Framework;
using Offtrack.Runtime;
using Offtrack.Runtime.Presentation;
using UnityEditor;
using UnityEngine;

namespace Offtrack.Tests.EditMode
{
    public sealed class M2AssetConfigurationTests
    {
        [Test]
        public void EnabledBuildScenesContainTheWholeJourneyInPlaybackOrder()
        {
            var paths = EditorBuildSettings.scenes.Where(scene => scene.enabled).Select(scene => scene.path).ToArray();
            Assert.That(paths, Is.EqualTo(OfftrackScenes.Journey.Select(OfftrackScenes.Path)));
            foreach (var path in paths)
                Assert.That(AssetDatabase.LoadAssetAtPath<SceneAsset>(path), Is.Not.Null, path);
        }

        [Test]
        public void SavedJourneyConfigurationHasTheSpecifiedDurationsAndWindowDefaults()
        {
            var config = AssetDatabase.LoadAssetAtPath<GameConfig>("Assets/Offtrack/Config/GameConfig.asset");
            Assert.That(config, Is.Not.Null);
            Assert.That(config.Cutscenes, Is.Not.Null);
            Assert.That(config.Cutscenes.OpeningDuration, Is.EqualTo(16));
            Assert.That(config.Cutscenes.Portrait.MontageEnd, Is.EqualTo(6));
            Assert.That(config.Cutscenes.Portrait.ClosingEnd, Is.EqualTo(13));
            Assert.That(config.Cutscenes.EndFrameHold, Is.EqualTo(.5));
            Assert.That(config.Cutscenes.ResizeStandaloneWindow, Is.True);
            Assert.That(config.Cutscenes.JourneyWindowWidth, Is.EqualTo(585));
            Assert.That(config.Cutscenes.JourneyWindowHeight, Is.EqualTo(1266));
            Assert.That(config.Cutscenes.ClosingContinueAfter, Is.Zero);
            Assert.That(config.Cutscenes.Timing, Is.Not.Null);
            Assert.That(config.Shell.BootDuration, Is.EqualTo(3.7));
            Assert.That(config.Shell.BootHoldDuration, Is.EqualTo(.6));
            Assert.That(config.Machine.StartDuration, Is.EqualTo(.4));
            Assert.That(config.Shell.SkipCountdownDuration, Is.EqualTo(8));
            Assert.That(config.Shell.StayResumeDelay, Is.EqualTo(0.54));
        }

        [Test]
        public void SavedFormulaRevealWidthsAreMeasuredAtTheAuthoredTypeSize()
        {
            var config = AssetDatabase.LoadAssetAtPath<GameConfig>("Assets/Offtrack/Config/GameConfig.asset");
            var formulas = Offtrack.Core.CutsceneTimeline.Formulas;
            var widths = config.Cutscenes.Timing.FormulaWidths;
            Assert.That(widths.Length, Is.EqualTo(formulas.Count));
            for (int index = 0; index < formulas.Count; index++)
            {
                // World units at 34 px. A width that carried a camera zoom would reveal the row
                // faster than the chalk writes it; the first delivery stored 411 for a 227 px row.
                Assert.That(widths[index], Is.EqualTo(Offtrack.Editor.M2Builder.MeasureFormulaWidth(formulas[index])).Within(1),
                    formulas[index]);
                Assert.That(widths[index], Is.InRange(formulas[index].Length * 34 * .25, formulas[index].Length * 34 * .6), formulas[index]);
            }
        }

        [Test]
        public void SharedDialogPrefabHasBothActionTargetsAndNoMissingBindings()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Offtrack/Prefabs/Dialog.prefab");
            Assert.That(prefab, Is.Not.Null);
            var dialog = prefab.GetComponent<DialogView>();
            Assert.That(dialog, Is.Not.Null);
            Assert.That(dialog.PrimaryButton, Is.Not.Null, "Lottery, retry and stay require an action target.");
            Assert.That(dialog.SecondaryButton, Is.Not.Null, "Giving up must remain a separately clickable action.");
            Assert.That(dialog.PrimaryButton, Is.Not.EqualTo(dialog.SecondaryButton));
            var serialized = new SerializedObject(dialog);
            var property = serialized.GetIterator();
            while (property.NextVisible(true))
            {
                if (property.propertyType == SerializedPropertyType.ObjectReference)
                    Assert.That(property.objectReferenceValue, Is.Not.Null, "Dialog binding is missing: " + property.propertyPath);
            }
            Assert.That(prefab.GetComponentsInChildren<MonoBehaviour>(true).All(component => component != null), Is.True,
                "Dialog prefab contains a missing script.");
        }

        [Test]
        public void ExistingDialogStatesUseTheSameWidthAndBottomPadding()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Offtrack/Prefabs/Dialog.prefab");
            Assert.That(typeof(ShellViewSettings).GetField("DialogBodyHeight"), Is.Null);
            Assert.That(typeof(ShellViewSettings).GetField("DialogBodyTop"), Is.Null);
            Assert.That(prefab.transform.Find("Dialog_Body").GetComponent<UnityEngine.UI.Text>().lineSpacing, Is.Not.EqualTo(1.5f));
            var config = AssetDatabase.LoadAssetAtPath<GameConfig>("Assets/Offtrack/Config/GameConfig.asset");
            Assert.That(config.Shell.DialogDebounceDuration, Is.EqualTo(.3f));
            var instance = Object.Instantiate(prefab);
            try
            {
                var card = instance.GetComponent<RectTransform>();
                var dialog = instance.GetComponent<DialogView>();
                foreach (var state in new[] { Offtrack.Core.AdShellState.Lottery, Offtrack.Core.AdShellState.FailDialog, Offtrack.Core.AdShellState.ConfirmSkip, Offtrack.Core.AdShellState.Guide, Offtrack.Core.AdShellState.Fragment })
                {
                    dialog.Show(state, 1, null);
                    Assert.That(card.sizeDelta.x, Is.EqualTo(608));
                    Assert.That(dialog.PrimaryButton.rect.height, Is.EqualTo(62));
                    var bottomPadding = dialog.PrimaryButton.anchoredPosition.y + dialog.PrimaryButton.rect.yMin - card.rect.yMin;
                    Assert.That(bottomPadding, Is.EqualTo(55).Within(.001));
                    bool split = state == Offtrack.Core.AdShellState.FailDialog || state == Offtrack.Core.AdShellState.ConfirmSkip;
                    Assert.That(dialog.SecondaryButton.gameObject.activeSelf, Is.EqualTo(split));
                    Assert.That(dialog.PrimaryButton.rect.width, Is.EqualTo(split ? 249 : 498));
                    if (split) Assert.That(dialog.SecondaryButton.anchoredPosition.x, Is.LessThan(dialog.PrimaryButton.anchoredPosition.x));
                }
            }
            finally { Object.DestroyImmediate(instance); }
        }
    }
}
