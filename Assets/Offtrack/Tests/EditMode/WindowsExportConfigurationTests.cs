using NUnit.Framework;
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
            Assert.That(PlayerSettings.defaultScreenWidth, Is.EqualTo(config.Cutscenes.WindowWidth));
            Assert.That(PlayerSettings.defaultScreenHeight, Is.EqualTo(config.Cutscenes.WindowHeight));
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
    }
}
