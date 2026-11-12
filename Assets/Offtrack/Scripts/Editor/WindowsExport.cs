using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using Offtrack.Runtime;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.Rendering;

namespace Offtrack.Editor
{
    /// <summary>Repeatable release export; all generated output stays under Builds.</summary>
    public static class WindowsExport
    {
        private const string OutputDirectory = "Builds/OFFTRACK-Windows";
        private const string ArchivePath = "Builds/OFFTRACK-Windows.zip";
        private const string ConfigPath = "Assets/Offtrack/Config/GameConfig.asset";
        internal static bool IsBuildingPlayer { get; private set; }
        private static readonly string[] JourneyScenes =
        {
            "Assets/Scenes/CutsceneA.unity", "Assets/Scenes/Main.unity", "Assets/Scenes/CutsceneB.unity"
        };

        public static void ApplyExportSettings()
        {
            var config = AssetDatabase.LoadAssetAtPath<GameConfig>(ConfigPath);
            if (config == null) throw new BuildFailedException("Missing OFFTRACK GameConfig.");
            PlayerSettings.productName = "OFFTRACK";
            PlayerSettings.defaultScreenWidth = config.Cutscenes.WindowWidth;
            PlayerSettings.defaultScreenHeight = config.Cutscenes.WindowHeight;
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Standalone, ScriptingImplementation.Mono2x);
            // This flat 2D Windows release needs no D3D12 features. D3D11 also avoids
            // the target driver's D3D12 info-queue diagnostic during player startup.
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.StandaloneWindows64, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.StandaloneWindows64, new[] { GraphicsDeviceType.Direct3D11 });
            // Equivalent to Renderer2D's Post-processing Enabled checkbox being off.
            // Prevent construction of unused passes referencing stripped post-process shaders.
            var renderer = AssetDatabase.LoadAssetAtPath<ScriptableObject>("Assets/Settings/Renderer2D.asset");
            if (renderer == null) throw new BuildFailedException("Missing OFFTRACK 2D renderer.");
            var rendererSettings = new SerializedObject(renderer);
            rendererSettings.FindProperty("m_PostProcessData").objectReferenceValue = null;
            rendererSettings.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(renderer);
            // Persist newly added configuration defaults through Unity's serializer.
            EditorUtility.SetDirty(config);
            AssetDatabase.SaveAssets();
        }

        [MenuItem("Offtrack/Export Windows Release")]
        public static void Build()
        {
            try
            {
                Export();
                if (Application.isBatchMode) EditorApplication.Exit(0);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                if (Application.isBatchMode) EditorApplication.Exit(1);
                else throw;
            }
        }

        private static void Export()
        {
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string output = Path.GetFullPath(Path.Combine(projectRoot, OutputDirectory));
            string archivePath = Path.Combine(projectRoot, ArchivePath);
            string buildsRoot = Path.GetFullPath(Path.Combine(projectRoot, "Builds")) + Path.DirectorySeparatorChar;
            if (!output.StartsWith(buildsRoot, StringComparison.OrdinalIgnoreCase))
                throw new BuildFailedException("Export output escaped the project's Builds directory.");
            // Even a failed preflight must not leave an old ZIP looking like this export's result.
            if (File.Exists(archivePath)) File.Delete(archivePath);
            if (Application.isBatchMode && !Environment.GetCommandLineArgs().Contains("--burst-disable-compilation"))
                throw new BuildFailedException("This machine requires --burst-disable-compilation in batchmode.");
            var scenes = EditorBuildSettings.scenes.Where(scene => scene.enabled).Select(scene => scene.path).ToArray();
            if (!scenes.SequenceEqual(JourneyScenes) || scenes.Any(scene => !File.Exists(scene)))
                throw new BuildFailedException("Build Settings must contain CutsceneA, Main, CutsceneB in that order.");
            ApplyExportSettings();

            // Clean only this export's known output, so repeated builds cannot ship stale files.
            if (Directory.Exists(output)) Directory.Delete(output, true);
            Directory.CreateDirectory(output);

            BuildReport report;
            try
            {
                IsBuildingPlayer = true;
                report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
                {
                    scenes = scenes,
                    locationPathName = Path.Combine(output, "OFFTRACK.exe"),
                    target = BuildTarget.StandaloneWindows64,
                    options = BuildOptions.DetailedBuildReport
                });
            }
            finally { IsBuildingPlayer = false; }
            Debug.Log($"[OFFTRACK Export] {report.summary.result}; errors={report.summary.totalErrors}; " +
                $"warnings={report.summary.totalWarnings}; bytes={report.summary.totalSize}; duration={report.summary.totalTime}");
            if (report.summary.result != BuildResult.Succeeded || report.summary.totalErrors != 0)
                throw new BuildFailedException("Windows release build failed; see the BuildReport above.");

            var editorAssets = report.packedAssets.SelectMany(pack => pack.contents)
                .Select(item => item.sourceAssetPath).Where(IsEditorOnlyAsset).Distinct().ToArray();
            if (editorAssets.Length != 0)
                throw new BuildFailedException("Editor-only assets leaked into Player: " + string.Join(", ", editorAssets));
            var forbiddenAssemblies = Directory.GetFiles(output, "*.dll", SearchOption.AllDirectories)
                .Where(path => PlayerAssemblyPolicy.IsForbiddenAssembly(Path.GetFileName(path))).ToArray();
            if (forbiddenAssemblies.Length != 0)
                throw new BuildFailedException("Test/editor assemblies leaked into Player: " + string.Join(", ", forbiddenAssemblies));
            string dataDirectory = Path.Combine(output, "OFFTRACK_Data");
            int removedInitializers = PlayerAssemblyPolicy.ReconcileExcludedInitializers(dataDirectory);
            Debug.Log("[OFFTRACK Export] Removed exported startup registrations for excluded fixtures: " + removedInitializers);
            PlayerAssemblyPolicy.ValidatePlayerMetadata(dataDirectory);

            string[] files = Directory.GetFiles(output, "*", SearchOption.AllDirectories)
                .Where(path => CanShip(path.Substring(output.Length + 1))).OrderBy(path => path, StringComparer.Ordinal).ToArray();
            long unpackedBytes = 0;
            string partialPath = archivePath + ".partial";
            string hash;
            try
            {
                using (var stream = File.Create(partialPath))
                using (var archive = new ZipArchive(stream, ZipArchiveMode.Create))
                {
                    foreach (string file in files)
                    {
                        string relative = file.Substring(output.Length + 1).Replace('\\', '/');
                        var entry = archive.CreateEntry("OFFTRACK/" + relative, System.IO.Compression.CompressionLevel.Optimal);
                        using (var source = File.OpenRead(file))
                        using (var destination = entry.Open()) source.CopyTo(destination);
                        unpackedBytes += new FileInfo(file).Length;
                    }
                }
                using (var sha = SHA256.Create())
                using (var stream = File.OpenRead(partialPath))
                    hash = BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
                File.Move(partialPath, archivePath);
            }
            finally
            {
                if (File.Exists(partialPath)) File.Delete(partialPath);
            }
            Debug.Log($"[OFFTRACK Export] Archive={ArchivePath}; files={files.Length}; " +
                $"compressedBytes={new FileInfo(archivePath).Length}; unpackedBytes={unpackedBytes}; SHA256={hash}");
        }

        private static bool IsEditorOnlyAsset(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            path = path.Replace('\\', '/');
            return path.StartsWith("docs/", StringComparison.OrdinalIgnoreCase)
                || path.StartsWith("Assets/Offtrack/Tests/", StringComparison.OrdinalIgnoreCase)
                || path.StartsWith("Assets/Offtrack/Tools/", StringComparison.OrdinalIgnoreCase)
                // Package MonoScript metadata can carry an Editor source path even though
                // its editor code is excluded. Inspect project assets here and actual DLLs above.
                || path.StartsWith("Assets/Offtrack/Scripts/Editor/", StringComparison.OrdinalIgnoreCase);
        }

        private static bool CanShip(string relative)
        {
            string[] parts = relative.Replace('\\', '/').Split('/');
            return !parts.Any(part => part.EndsWith("_BurstDebugInformation_DoNotShip", StringComparison.OrdinalIgnoreCase)
                || part.EndsWith("_BackUpThisFolder_ButDontShipItWithYourGame", StringComparison.OrdinalIgnoreCase)
                || part.Equals("docs", StringComparison.OrdinalIgnoreCase))
                && !relative.EndsWith(".pdb", StringComparison.OrdinalIgnoreCase)
                && !relative.EndsWith(".log", StringComparison.OrdinalIgnoreCase)
                && !relative.EndsWith(".mdb", StringComparison.OrdinalIgnoreCase);
        }
    }
}
