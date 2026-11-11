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
            Assert.That(paths, Is.EqualTo(new[]
            {
                "Assets/Scenes/CutsceneA.unity", "Assets/Scenes/Main.unity", "Assets/Scenes/CutsceneB.unity"
            }));
            foreach (var path in paths)
                Assert.That(AssetDatabase.LoadAssetAtPath<SceneAsset>(path), Is.Not.Null, path);
        }

        [Test]
        public void SavedJourneyConfigurationHasTheSpecifiedDurationsAndWindowDefaults()
        {
            var config = AssetDatabase.LoadAssetAtPath<GameConfig>("Assets/Offtrack/Config/GameConfig.asset");
            Assert.That(config, Is.Not.Null);
            Assert.That(config.Cutscenes, Is.Not.Null);
            Assert.That(config.Cutscenes.OpeningDuration, Is.EqualTo(15));
            Assert.That(config.Cutscenes.ClosingDuration, Is.EqualTo(12));
            Assert.That(config.Cutscenes.EndFrameHold, Is.EqualTo(.5));
            Assert.That(config.Cutscenes.ResizeStandaloneWindow, Is.True);
            Assert.That(config.Cutscenes.WindowWidth, Is.EqualTo(1280));
            Assert.That(config.Cutscenes.WindowHeight, Is.EqualTo(720));
            Assert.That(config.Cutscenes.MainWindowWidth, Is.EqualTo(585));
            Assert.That(config.Cutscenes.MainWindowHeight, Is.EqualTo(1266));
            Assert.That(config.Cutscenes.Timing, Is.Not.Null);
            Assert.That(config.Shell.BootDuration, Is.EqualTo(1));
            Assert.That(config.Shell.SkipCountdownDuration, Is.EqualTo(8));
            Assert.That(config.Shell.CompleteDuration, Is.EqualTo(3));
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
    }
}
