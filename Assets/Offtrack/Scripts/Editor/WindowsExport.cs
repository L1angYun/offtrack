using System;
using System.Collections.Generic;
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
    /// <summary>What one export entry leaves under the build root.</summary>
    public sealed class ExportVariant
    {
        public string Folder { get; }
        /// <summary>File name of the packaged archive; null when the export is not packaged.</summary>
        public string Archive { get; }
        public IReadOnlyList<string> ScriptingDefines { get; }
        public bool CarriesSmokeHook => ScriptingDefines.Contains(PlayerAssemblyPolicy.SmokeDefine);

        internal ExportVariant(string folder, string archive, params string[] scriptingDefines)
        {
            Folder = folder;
            Archive = archive;
            ScriptingDefines = Array.AsReadOnly(scriptingDefines);
        }
    }

    /// <summary>
    /// Repeatable exports under a validated configurable root (project Builds by default):
    /// the release, and a smoke player that differs from it only by the smoke define.
    /// </summary>
    public static class WindowsExport
    {
        private const string ConfigPath = "Assets/Offtrack/Config/GameConfig.asset";
        internal static bool IsBuildingPlayer { get; private set; }

        public static ExportVariant Release { get; } = new ExportVariant("OFFTRACK-Windows", "OFFTRACK-Windows.zip");
        /// <summary>
        /// The release sources and settings plus the smoke define, in a folder of its own.
        /// It is never packaged: only the release archive is a deliverable.
        /// </summary>
        public static ExportVariant Smoke { get; } = new ExportVariant("OFFTRACK-Smoke", null, PlayerAssemblyPolicy.SmokeDefine);

        public static void ApplyExportSettings()
        {
            var config = AssetDatabase.LoadAssetAtPath<GameConfig>(ConfigPath);
            if (config == null) throw new BuildFailedException("Missing OFFTRACK GameConfig.");
            PlayerSettings.productName = "OFFTRACK";
            PlayerSettings.defaultScreenWidth = config.Cutscenes.JourneyWindowWidth;
            PlayerSettings.defaultScreenHeight = config.Cutscenes.JourneyWindowHeight;
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
        public static void Build() => Run(Release);

        [MenuItem("Offtrack/Export Windows Smoke Player")]
        public static void BuildSmoke() => Run(Smoke);

        public static BuildPlayerOptions PlayerOptions(ExportVariant variant, string[] scenes, string output)
        {
            var options = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = Path.Combine(output, "OFFTRACK.exe"),
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.DetailedBuildReport
            };
            if (variant.ScriptingDefines.Count != 0) options.extraScriptingDefines = variant.ScriptingDefines.ToArray();
            return options;
        }

        /// <summary>
        /// Removes the earlier result that a failed export would otherwise leave looking current:
        /// the archive of a packaged export, the player folder of an unpackaged one.
        /// </summary>
        public static void InvalidatePreviousResult(ExportVariant variant, BuildRootResolution resolution)
        {
            if (variant.Archive != null)
            {
                ExportOutput.DeleteFile(Path.Combine(resolution.Root, variant.Archive), resolution);
                return;
            }
            string output = Path.Combine(resolution.Root, variant.Folder);
            ExportOutput.RequireProjectOutside(output, resolution);
            ExportOutput.Clean(output, resolution);
        }

        /// <summary>Empties this variant's own folder and nothing else under the build root.</summary>
        public static string PrepareOutput(ExportVariant variant, BuildRootResolution resolution)
        {
            string output = Path.Combine(resolution.Root, variant.Folder);
            ExportOutput.RequireProjectOutside(output, resolution);
            // Clean only this export's known output, so repeated builds cannot ship stale files.
            ExportOutput.Clean(output, resolution);
            ExportOutput.CreateDirectory(output, resolution);
            return output;
        }

        /// <summary>
        /// The release must not carry the smoke hook; a smoke player without it proves nothing.
        /// Returns the hook evidence found in a smoke player, and nothing for a release.
        /// </summary>
        public static string[] VerifySmokeHook(ExportVariant variant, string dataDirectory)
        {
            if (variant.CarriesSmokeHook) return PlayerAssemblyPolicy.RequireSmokeHooks(dataDirectory);
            PlayerAssemblyPolicy.RejectSmokeHooks(dataDirectory);
            return Array.Empty<string>();
        }

        private static void Run(ExportVariant variant)
        {
            try
            {
                Export(variant);
                if (Application.isBatchMode) EditorApplication.Exit(0);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                if (Application.isBatchMode) EditorApplication.Exit(1);
                else throw;
            }
        }

        private static void Export(ExportVariant variant)
        {
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string[] arguments = Environment.GetCommandLineArgs();
            string settingsPath = Path.Combine(projectRoot, "UserSettings", "OfftrackBuildRoot.txt");
            string settings = null;
            if (!BuildRootResolver.HasCommandLineOverride(arguments))
            {
                try
                {
                    settings = BuildRootResolver.DecodeSettings(File.ReadAllBytes(settingsPath));
                }
                catch (FileNotFoundException) { }
                catch (DirectoryNotFoundException) { }
                catch (IOException exception)
                {
                    throw new BuildFailedException("Cannot read settings file " + BuildRootResolver.SettingsFileName
                        + " (raw value unavailable): " + exception.Message);
                }
                catch (UnauthorizedAccessException exception)
                {
                    throw new BuildFailedException("Cannot read settings file " + BuildRootResolver.SettingsFileName
                        + " (raw value unavailable): " + exception.Message);
                }
            }
            var resolution = BuildRootResolver.ResolveWithSource(projectRoot, arguments, settings);
            // Even a failed preflight must not leave an old result looking like this export's.
            InvalidatePreviousResult(variant, resolution);
            if (Application.isBatchMode && !arguments.Contains("--burst-disable-compilation"))
                throw new BuildFailedException("This machine requires --burst-disable-compilation in batchmode.");
            var scenes = EditorBuildSettings.scenes.Where(scene => scene.enabled).Select(scene => scene.path).ToArray();
            if (!scenes.SequenceEqual(OfftrackScenes.Journey.Select(OfftrackScenes.Path)) || scenes.Any(scene => !File.Exists(scene)))
                throw new BuildFailedException($"Build Settings must contain {string.Join(", ", OfftrackScenes.Journey)} in that order.");
            ApplyExportSettings();

            string output = PrepareOutput(variant, resolution);

            BuildReport report;
            try
            {
                IsBuildingPlayer = true;
                report = BuildPipeline.BuildPlayer(PlayerOptions(variant, scenes, output));
            }
            finally { IsBuildingPlayer = false; }
            Debug.Log($"[OFFTRACK Export] {report.summary.result}; errors={report.summary.totalErrors}; " +
                $"warnings={report.summary.totalWarnings}; bytes={report.summary.totalSize}; duration={report.summary.totalTime}");
            if (report.summary.result != BuildResult.Succeeded || report.summary.totalErrors != 0)
                throw new BuildFailedException("Windows " + (variant.CarriesSmokeHook ? "smoke" : "release")
                    + " build failed; see the BuildReport above.");

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
            string[] hooks = VerifySmokeHook(variant, dataDirectory);
            // Recorded so each export's game assemblies can be identified by content afterwards.
            var gameAssemblies = Directory.GetFiles(Path.Combine(dataDirectory, "Managed"), "Offtrack*.dll")
                .OrderBy(path => path, StringComparer.Ordinal)
                .Select(path => Path.GetFileName(path) + " SHA256=" + Sha256(path));
            Debug.Log($"[OFFTRACK Export] Smoke hook: {(hooks.Length == 0 ? "none" : string.Join("; ", hooks))}; " +
                $"extra defines: {(variant.ScriptingDefines.Count == 0 ? "none" : string.Join(",", variant.ScriptingDefines))}; " +
                string.Join("; ", gameAssemblies));

            string[] files = Directory.GetFiles(output, "*", SearchOption.AllDirectories)
                .Where(path => CanShip(path.Substring(output.Length + 1))).OrderBy(path => path, StringComparer.Ordinal).ToArray();
            if (variant.Archive == null)
            {
                Debug.Log($"[OFFTRACK Export] Unpackaged player={output}; files={files.Length}; " +
                    $"unpackedBytes={files.Sum(file => new FileInfo(file).Length)}");
                return;
            }

            string archivePath = Path.Combine(resolution.Root, variant.Archive);
            long unpackedBytes = 0;
            string partialPath = archivePath + ".partial";
            string hash;
            // Only clean up a partial that this export actually created.
            FileStream partialStream = ExportOutput.CreatePartialArchive(partialPath, resolution);
            bool archiveCompleted = false;
            try
            {
                using (var stream = partialStream)
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
                hash = Sha256(partialPath);
                ExportOutput.Move(partialPath, archivePath, resolution);
                archiveCompleted = true;
            }
            finally
            {
                ExportOutput.CleanupPartial(partialPath, resolution, !archiveCompleted);
            }
            Debug.Log($"[OFFTRACK Export] Archive={archivePath}; files={files.Length}; " +
                $"compressedBytes={new FileInfo(archivePath).Length}; unpackedBytes={unpackedBytes}; SHA256={hash}");
        }

        private static string Sha256(string path)
        {
            using (var sha = SHA256.Create())
            using (var stream = File.OpenRead(path))
                return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
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
