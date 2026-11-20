using System.IO;
using NUnit.Framework;
using Offtrack.Editor;
using Offtrack.Runtime;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;
using UnityEngine.Rendering;

namespace Offtrack.Tests.EditMode
{
    public sealed class WindowsExportConfigurationTests
    {
        [Test]
        public void StartupWindowMatchesTheOpeningScene()
        {
            var config = AssetDatabase.LoadAssetAtPath<GameConfig>("Assets/Offtrack/Config/GameConfig.asset");
            Assert.That(PlayerSettings.defaultScreenWidth, Is.EqualTo(config.Cutscenes.JourneyWindowWidth));
            Assert.That(PlayerSettings.defaultScreenHeight, Is.EqualTo(config.Cutscenes.JourneyWindowHeight));
            Assert.That(PlayerSettings.productName, Is.EqualTo("OFFTRACK"));
        }

        [Test]
        public void WindowsExportKeepsTheMonoBackend()
        {
            Assert.That(PlayerSettings.GetScriptingBackend(NamedBuildTarget.Standalone),
                Is.EqualTo(ScriptingImplementation.Mono2x));
        }

        [Test]
        public void WindowsRendererUsesDirect3D11WithoutUnusedPostProcessing()
        {
            Assert.That(PlayerSettings.GetUseDefaultGraphicsAPIs(BuildTarget.StandaloneWindows64), Is.False);
            Assert.That(PlayerSettings.GetGraphicsAPIs(BuildTarget.StandaloneWindows64),
                Is.EqualTo(new[] { GraphicsDeviceType.Direct3D11 }));
            var renderer = AssetDatabase.LoadAssetAtPath<ScriptableObject>("Assets/Settings/Renderer2D.asset");
            Assert.That(new SerializedObject(renderer).FindProperty("m_PostProcessData").objectReferenceValue, Is.Null);
        }

        [Test]
        public void ReleaseExportKeepsItsFolderAndArchiveAndAddsNoDefine()
        {
            Assert.That(WindowsExport.Release.Folder, Is.EqualTo("OFFTRACK-Windows"));
            Assert.That(WindowsExport.Release.Archive, Is.EqualTo("OFFTRACK-Windows.zip"));
            Assert.That(WindowsExport.Release.ScriptingDefines, Is.Empty);
            Assert.That(WindowsExport.Release.CarriesSmokeHook, Is.False);
        }

        [Test]
        public void SmokeExportHasItsOwnFolderNoArchiveAndOnlyTheSmokeDefine()
        {
            Assert.That(WindowsExport.Smoke.Folder, Is.EqualTo("OFFTRACK-Smoke"));
            Assert.That(WindowsExport.Smoke.Archive, Is.Null);
            Assert.That(WindowsExport.Smoke.ScriptingDefines, Is.EqualTo(new[] { "OFFTRACK_PLAYER_SMOKE" }));
            Assert.That(WindowsExport.Smoke.CarriesSmokeHook, Is.True);
        }

        [Test]
        public void SmokePlayerOptionsDifferFromTheReleaseOnlyByTheDefineAndFolder()
        {
            var scenes = new[] { "Assets/Scenes/Title.unity", "Assets/Scenes/Main.unity" };
            var release = WindowsExport.PlayerOptions(WindowsExport.Release, scenes, Path.Combine("Root", "OFFTRACK-Windows"));
            var smoke = WindowsExport.PlayerOptions(WindowsExport.Smoke, scenes, Path.Combine("Root", "OFFTRACK-Smoke"));

            Assert.That(release.extraScriptingDefines, Is.Null.Or.Empty);
            Assert.That(smoke.extraScriptingDefines, Is.EqualTo(new[] { "OFFTRACK_PLAYER_SMOKE" }));
            Assert.That(release.locationPathName, Is.EqualTo(Path.Combine("Root", "OFFTRACK-Windows", "OFFTRACK.exe")));
            Assert.That(smoke.locationPathName, Is.EqualTo(Path.Combine("Root", "OFFTRACK-Smoke", "OFFTRACK.exe")));
            foreach (var options in new[] { release, smoke })
            {
                Assert.That(options.scenes, Is.EqualTo(new[] { "Assets/Scenes/Title.unity", "Assets/Scenes/Main.unity" }));
                Assert.That(options.target, Is.EqualTo(BuildTarget.StandaloneWindows64));
                Assert.That(options.options, Is.EqualTo(BuildOptions.DetailedBuildReport));
            }
            Assert.That(smoke.targetGroup, Is.EqualTo(release.targetGroup));
            Assert.That(smoke.subtarget, Is.EqualTo(release.subtarget));
            Assert.That(smoke.assetBundleManifestPath, Is.EqualTo(release.assetBundleManifestPath));
        }

        [Test]
        public void ProjectWideDefinesDoNotEnableTheSmokeHook()
        {
            Assert.That(PlayerSettings.GetScriptingDefineSymbols(NamedBuildTarget.Standalone),
                Does.Not.Contain("OFFTRACK_PLAYER_SMOKE"));
        }
    }
}
