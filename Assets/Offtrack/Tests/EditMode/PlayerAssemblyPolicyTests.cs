using System;
using System.IO;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;
using NUnit.Framework;
using Offtrack.Editor;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Compilation;

namespace Offtrack.Tests.EditMode
{
    public sealed class PlayerAssemblyPolicyTests
    {
        // Registrations in the schema Unity writes to an exported RuntimeInitializeOnLoads.json.
        private const string GameRegistration = "{\"assemblyName\":\"Offtrack.Runtime\",\"nameSpace\":\"Offtrack.Runtime\",\"className\":\"SceneTransition\",\"methodName\":\"EnsureInstance\",\"loadTypes\":1,\"isUnityClass\":false}";
        private const string SmokeRegistration = "{\"assemblyName\":\"Offtrack.Runtime\",\"nameSpace\":\"Offtrack.Runtime\",\"className\":\"JourneySmoke\",\"methodName\":\"Launch\",\"loadTypes\":0,\"isUnityClass\":false}";
        private const string SmokePrepareRegistration = "{\"assemblyName\":\"Offtrack.Runtime\",\"nameSpace\":\"Offtrack.Runtime\",\"className\":\"JourneySmoke\",\"methodName\":\"Prepare\",\"loadTypes\":3,\"isUnityClass\":false}";
        private const string GameLiteral = "OFFTRACK window resize:";

        [TestCase("Unity.Collections.Tests.CoreCLR.InternalJobNestedPrivate.dll")]
        [TestCase("Unity.Collections.Tests.CoreCLR.PrivateJobNested.dll")]
        [TestCase("Unity.Collections.Tests.CoreCLR.ProtectedJobNested.dll")]
        [TestCase("Unity.Collections.Tests.CoreCLR.PublicJobPrivateGeneric.dll")]
        [TestCase("Package.Tests.dll")]
        [TestCase("NewPackage.Tests.Helpers.dll")]
        [TestCase("NewPackageTests.dll")]
        [TestCase("newpackage.tEsTs.dll")]
        public void AnyTestsNameIsForbiddenInTheDeliveredPlayer(string name)
        {
            Assert.That(PlayerAssemblyPolicy.IsForbiddenAssembly(name), Is.True);
        }

        [TestCase("Unity.Tutorials.dll")]
        [TestCase("Unity.2D.Welcome.dll")]
        [TestCase("Unity.VisualScripting.DocCodeExamples.dll")]
        public void DisclosedTemplateAssembliesAreRetained(string name)
        {
            Assert.That(PlayerAssemblyPolicy.IsForbiddenAssembly(name), Is.False);
        }

        [Test]
        public void OnlyTheFourAuditedPackageFixturesAreRemoved()
        {
            string core = CompilationPipeline.GetAssemblies(AssembliesType.Editor)
                .Single(assembly => assembly.name == "Offtrack.Core").outputPath;
            var result = PlayerAssemblyPolicy.FilterForRelease(new[]
            {
                core,
                "Unity.Collections.Tests.CoreCLR.InternalJobNestedPrivate.dll",
                "Unity.Collections.Tests.CoreCLR.PrivateJobNested.dll",
                "Unity.Collections.Tests.CoreCLR.ProtectedJobNested.dll",
                "Unity.Collections.Tests.CoreCLR.PublicJobPrivateGeneric.dll"
            });
            Assert.That(result, Is.EqualTo(new[] { core }));
        }

        [Test]
        public void UnknownTestAssemblyFailsInsteadOfBeingSilentlyRemoved()
        {
            Assert.Throws<BuildFailedException>(() => PlayerAssemblyPolicy.FilterForRelease(
                new[] { "Unreviewed.Package.Tests.dll" }));
        }

        [Test]
        public void ANewIncomingReferenceToAnExcludedFixtureFailsTheBuild()
        {
            string directory = Path.GetFullPath("Logs/E1-fix1-assembly-tests");
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, "DependencyProbe-" + Guid.NewGuid().ToString("N") + ".dll");
            try
            {
                using (var assembly = AssemblyDefinition.CreateAssembly(
                    new AssemblyNameDefinition("DependencyProbe", new Version(1, 0)), "DependencyProbe", ModuleKind.Dll))
                {
                    assembly.MainModule.AssemblyReferences.Add(new AssemblyNameReference(
                        "Unity.Collections.Tests.CoreCLR.PrivateJobNested", new Version(1, 0)));
                    assembly.Write(path);
                }
                Assert.Throws<BuildFailedException>(() => PlayerAssemblyPolicy.FilterForRelease(new[] { path }));
            }
            finally { File.Delete(path); }
        }

        [Test]
        public void RenamingATestDllDoesNotBypassTheAssemblyIdentityCheck()
        {
            string directory = Path.GetFullPath("Logs/E1-fix1-assembly-tests");
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, "Renamed-" + Guid.NewGuid().ToString("N") + ".dll");
            try
            {
                using (var assembly = AssemblyDefinition.CreateAssembly(
                    new AssemblyNameDefinition("Package.Tests", new Version(1, 0)), "Renamed", ModuleKind.Dll))
                    assembly.Write(path);
                Assert.Throws<BuildFailedException>(() => PlayerAssemblyPolicy.FilterForRelease(new[] { path }));
            }
            finally { File.Delete(path); }
        }

        [TestCase(true)]
        [TestCase(false)]
        public void TestNamesRemainingInEitherGeneratedMetadataFileFailTheBuild(bool scriptList)
        {
            string directory = Path.GetFullPath("Logs/E1-fix1-metadata-tests/" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            string scriptPath = Path.Combine(directory, "ScriptingAssemblies.json");
            string initPath = Path.Combine(directory, "RuntimeInitializeOnLoads.json");
            try
            {
                File.WriteAllText(scriptPath, "{\"names\":[\"" + (scriptList ? "Package.Tests.dll" : "Unity.Tutorials.dll") + "\"]}");
                File.WriteAllText(initPath, "{\"root\":[{\"assemblyName\":\"" + (scriptList ? "Unity.Tutorials" : "Package.Tests") + "\"}]}");
                Assert.Throws<BuildFailedException>(() => PlayerAssemblyPolicy.ValidatePlayerMetadata(directory));
            }
            finally
            {
                File.Delete(scriptPath);
                File.Delete(initPath);
                Directory.Delete(directory);
            }
        }

        [Test]
        public void OtherBuildsAndTestPlayersAreNotFilteredOutsideTheExportEntry()
        {
            var original = new[] { "Unrelated.Tests.dll" };
            Assert.That(new ReleaseAssemblyFilter().OnFilterAssemblies(BuildOptions.IncludeTestAssemblies, original),
                Is.SameAs(original));
        }

        [Test]
        public void ReconciliationPreservesEveryFieldAndOrderOfRetainedInitializers()
        {
            WithMetadataDirectory(directory =>
            {
                const string retained = "{\"assemblyName\":\"Unity.Tutorials\",\"nameSpace\":\"Tutorials\",\"className\":\"Tracker\",\"methodName\":\"ResetStaticsOnLoad\",\"loadTypes\":4,\"isUnityClass\":false,\"future\":{\"text\":\"brace } { and \\\"quote\\\"\"}}";
                const string second = "{\"assemblyName\":\"UnityEngine.CoreModule\",\"methodName\":\"KeepSecond\"}";
                const string excluded = "{\"assemblyName\":\"Unity.Collections.Tests.CoreCLR.PrivateJobNested\",\"className\":\"__JobReflectionRegistrationOutput__123\",\"methodName\":\"EarlyInit\",\"loadTypes\":2}";
                string path = Path.Combine(directory, "RuntimeInitializeOnLoads.json");
                File.WriteAllText(path, "{ \"root\": [ " + retained + "," + excluded + "," + second + " ], \"futureRoot\":17}");
                Assert.That(PlayerAssemblyPolicy.ReconcileExcludedInitializers(directory), Is.EqualTo(1));
                string expected = "{ \"root\": [" + retained + "," + second + "], \"futureRoot\":17}";
                Assert.That(File.ReadAllText(path), Is.EqualTo(expected));
                Assert.That(PlayerAssemblyPolicy.ReconcileExcludedInitializers(directory), Is.Zero);
                Assert.That(File.ReadAllText(path), Is.EqualTo(expected));
                Assert.DoesNotThrow(() => PlayerAssemblyPolicy.ValidatePlayerMetadata(directory));
            });
        }

        [TestCase(true)]
        [TestCase(false)]
        public void ReconciliationRefusesToHideRegistrationsForAnIncludedFixture(bool listed)
        {
            WithMetadataDirectory(directory =>
            {
                const string fixture = "Unity.Collections.Tests.CoreCLR.PrivateJobNested.dll";
                string managed = Path.Combine(directory, "Managed");
                string dll = Path.Combine(managed, fixture);
                if (listed)
                    File.WriteAllText(Path.Combine(directory, "ScriptingAssemblies.json"), "{\"names\":[\"" + fixture + "\"]}");
                else
                {
                    Directory.CreateDirectory(managed);
                    File.WriteAllBytes(dll, Array.Empty<byte>());
                }
                try { Assert.Throws<BuildFailedException>(() => PlayerAssemblyPolicy.ReconcileExcludedInitializers(directory)); }
                finally
                {
                    if (File.Exists(dll)) File.Delete(dll);
                    if (Directory.Exists(managed)) Directory.Delete(managed);
                }
            });
        }

        [Test]
        public void ReleaseRejectsAStartupRegistrationOfTheSmokeHook()
        {
            WithPlayerData(GameRegistration + "," + SmokeRegistration, directory =>
            {
                WriteAssembly(directory, "Offtrack.Runtime.dll", "SceneTransition", GameLiteral);
                var exception = Assert.Throws<BuildFailedException>(() => PlayerAssemblyPolicy.RejectSmokeHooks(directory));
                Assert.That(exception.Message, Does.Contain("startup registration Offtrack.Runtime:JourneySmoke.Launch"));
            });
            // The same Player without that one registration is a valid release.
            WithPlayerData(GameRegistration, directory =>
            {
                WriteAssembly(directory, "Offtrack.Runtime.dll", "SceneTransition", GameLiteral);
                Assert.DoesNotThrow(() => PlayerAssemblyPolicy.RejectSmokeHooks(directory));
            });
        }

        [TestCase("Offtrack.Runtime.dll")]
        [TestCase("Offtrack.Core.dll")]
        public void ReleaseRejectsASmokeSwitchCompiledIntoAGameAssembly(string carrier)
        {
            WithPlayerData(GameRegistration, directory =>
            {
                WriteAssembly(directory, "Offtrack.Runtime.dll", "SceneTransition", GameLiteral);
                WriteAssembly(directory, carrier, "Startup", "--offtrack-smoke-claim");
                var exception = Assert.Throws<BuildFailedException>(() => PlayerAssemblyPolicy.RejectSmokeHooks(directory));
                Assert.That(exception.Message, Does.Contain("\"--offtrack-smoke\" in " + carrier));
            });
            WithPlayerData(GameRegistration, directory =>
            {
                WriteAssembly(directory, "Offtrack.Runtime.dll", "SceneTransition", GameLiteral);
                WriteAssembly(directory, carrier, "Startup", "--offtrackBuildRoot");
                Assert.DoesNotThrow(() => PlayerAssemblyPolicy.RejectSmokeHooks(directory));
            });
        }

        [Test]
        public void ReleaseRejectsTheSmokeHookTypeEvenWithoutARegistrationOrSwitch()
        {
            WithPlayerData(GameRegistration, directory =>
            {
                WriteAssembly(directory, "Offtrack.Runtime.dll", "JourneySmoke", GameLiteral);
                var exception = Assert.Throws<BuildFailedException>(() => PlayerAssemblyPolicy.RejectSmokeHooks(directory));
                Assert.That(exception.Message, Does.Contain("\"JourneySmoke\" in Offtrack.Runtime.dll"));
            });
        }

        [Test]
        public void ReleaseCannotBeVouchedForWithoutItsRuntimeAssembly()
        {
            WithPlayerData(GameRegistration, directory =>
            {
                // Another game assembly is present, but not the one the hook lives in.
                WriteAssembly(directory, "Offtrack.Core.dll", "ChartPlanner", GameLiteral);
                var exception = Assert.Throws<BuildFailedException>(() => PlayerAssemblyPolicy.RejectSmokeHooks(directory));
                Assert.That(exception.Message, Does.Contain("Cannot inspect Offtrack.Runtime.dll"));
            });
        }

        [TestCase(false, false)]
        [TestCase(true, false)]
        [TestCase(false, true)]
        public void SmokePlayerMissingAnyPartOfItsHookFailsTheSmokeExport(bool registered, bool compiled)
        {
            WithPlayerData(registered ? GameRegistration + "," + SmokeRegistration : GameRegistration, directory =>
            {
                WriteAssembly(directory, "Offtrack.Runtime.dll", compiled ? "JourneySmoke" : "SceneTransition",
                    compiled ? "--offtrack-smoke-claim" : GameLiteral);
                var exception = Assert.Throws<BuildFailedException>(() => PlayerAssemblyPolicy.RequireSmokeHooks(directory));
                Assert.That(exception.Message, Does.Contain("OFFTRACK_PLAYER_SMOKE did not take effect"));
            });
        }

        [Test]
        public void SmokePlayerWithItsHookReportsTheEvidence()
        {
            WithPlayerData(GameRegistration + "," + SmokeRegistration, directory =>
            {
                WriteAssembly(directory, "Offtrack.Runtime.dll", "JourneySmoke", "--offtrack-smoke-claim");
                Assert.That(PlayerAssemblyPolicy.RequireSmokeHooks(directory), Is.EqualTo(new[]
                {
                    "startup registration Offtrack.Runtime:JourneySmoke.Launch",
                    "\"--offtrack-smoke\" in Offtrack.Runtime.dll",
                    "\"JourneySmoke\" in Offtrack.Runtime.dll"
                }));
                // The release policy refuses this very Player.
                Assert.Throws<BuildFailedException>(() => PlayerAssemblyPolicy.RejectSmokeHooks(directory));
            });
        }

        [Test]
        public void EachExportEntryAppliesItsOwnSmokeHookRule()
        {
            // A Player compiled with the smoke define: both startup registrations and the switches.
            WithPlayerData(SmokePrepareRegistration + "," + GameRegistration + "," + SmokeRegistration, directory =>
            {
                WriteAssembly(directory, "Offtrack.Runtime.dll", "JourneySmoke", "--offtrack-smoke-fresh-window");
                var exception = Assert.Throws<BuildFailedException>(
                    () => WindowsExport.VerifySmokeHook(WindowsExport.Release, directory));
                Assert.That(exception.Message, Does.StartWith("Smoke hook leaked into the release Player: "));
                Assert.That(exception.Message, Does.Contain("startup registration Offtrack.Runtime:JourneySmoke.Prepare"));
                Assert.That(WindowsExport.VerifySmokeHook(WindowsExport.Smoke, directory), Is.EqualTo(new[]
                {
                    "startup registration Offtrack.Runtime:JourneySmoke.Prepare",
                    "startup registration Offtrack.Runtime:JourneySmoke.Launch",
                    "\"--offtrack-smoke\" in Offtrack.Runtime.dll",
                    "\"JourneySmoke\" in Offtrack.Runtime.dll"
                }));
            });
            // The same sources compiled without it.
            WithPlayerData(GameRegistration, directory =>
            {
                WriteAssembly(directory, "Offtrack.Runtime.dll", "SceneTransition", GameLiteral);
                Assert.That(WindowsExport.VerifySmokeHook(WindowsExport.Release, directory), Is.Empty);
                var exception = Assert.Throws<BuildFailedException>(
                    () => WindowsExport.VerifySmokeHook(WindowsExport.Smoke, directory));
                Assert.That(exception.Message, Does.Contain("OFFTRACK_PLAYER_SMOKE did not take effect"));
            });
        }

        [Test]
        public void SmokeSwitchesExistInGameSourcesOnlyBehindTheSmokeDefine()
        {
            const string guard = "#if OFFTRACK_PLAYER_SMOKE && UNITY_STANDALONE && !UNITY_EDITOR";
            var carriers = new[] { "Assets/Offtrack/Scripts/Core", "Assets/Offtrack/Scripts/Runtime" }
                .SelectMany(folder => Directory.GetFiles(folder, "*.cs", SearchOption.AllDirectories))
                .Where(path => File.ReadAllText(path).Contains("--offtrack-smoke"))
                .Select(path => path.Replace('\\', '/')).ToArray();
            Assert.That(carriers, Is.EqualTo(new[] { "Assets/Offtrack/Scripts/Runtime/JourneySmoke.cs" }));
            Assert.That(IsWrappedBy(File.ReadAllText(carriers[0]), guard), Is.True);

            // Negative controls: the guard that shipped the hook in every standalone Player,
            // code after the block, and an #else branch that compiles without the define.
            Assert.That(IsWrappedBy("#if UNITY_STANDALONE && !UNITY_EDITOR\nclass JourneySmoke { }\n#endif\n", guard), Is.False);
            Assert.That(IsWrappedBy(guard + "\nclass A { }\n#endif\nclass JourneySmoke { }\n", guard), Is.False);
            Assert.That(IsWrappedBy(guard + "\nclass A { }\n#else\nclass JourneySmoke { }\n#endif\n", guard), Is.False);
            Assert.That(IsWrappedBy(guard + "\r\nclass JourneySmoke { }\r\n#endif\r\n", guard), Is.True);
        }

        // One conditional block spans the whole file: no other branch and nothing outside it.
        private static bool IsWrappedBy(string source, string guard)
        {
            var lines = source.TrimStart('\uFEFF').Split('\n').Select(line => line.Trim())
                .Where(line => line.Length != 0).ToArray();
            return lines.Length >= 2 && lines[0] == guard && lines[lines.Length - 1] == "#endif"
                && lines.Count(line => line.StartsWith("#if", StringComparison.Ordinal)) == 1
                && lines.Count(line => line.StartsWith("#endif", StringComparison.Ordinal)) == 1
                && !lines.Any(line => line.StartsWith("#el", StringComparison.Ordinal));
        }

        private static void WithPlayerData(string registrations, Action<string> action)
        {
            string directory = Path.Combine(Path.GetTempPath(), "OFFTRACK-V3-Smoke-Policy-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(directory, "Managed"));
            try
            {
                File.WriteAllText(Path.Combine(directory, "RuntimeInitializeOnLoads.json"), "{\"root\":[" + registrations + "]}");
                action(directory);
            }
            finally { Directory.Delete(directory, true); }
        }

        // A real assembly: the literal lands in its user-string heap, the type name in its metadata.
        private static void WriteAssembly(string dataDirectory, string file, string typeName, string literal)
        {
            string name = Path.GetFileNameWithoutExtension(file);
            using (var assembly = AssemblyDefinition.CreateAssembly(
                new AssemblyNameDefinition(name, new Version(1, 0)), name, ModuleKind.Dll))
            {
                var module = assembly.MainModule;
                var type = new TypeDefinition(name, typeName,
                    TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed, module.TypeSystem.Object);
                var method = new MethodDefinition("Text", MethodAttributes.Public | MethodAttributes.Static, module.TypeSystem.String);
                var body = method.Body.GetILProcessor();
                body.Emit(OpCodes.Ldstr, literal);
                body.Emit(OpCodes.Ret);
                type.Methods.Add(method);
                module.Types.Add(type);
                assembly.Write(Path.Combine(dataDirectory, "Managed", file));
            }
        }

        private static void WithMetadataDirectory(Action<string> action)
        {
            string directory = Path.GetFullPath("Logs/E1-fix1-metadata-tests/" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            string scripts = Path.Combine(directory, "ScriptingAssemblies.json");
            string initializers = Path.Combine(directory, "RuntimeInitializeOnLoads.json");
            try
            {
                File.WriteAllText(scripts, "{\"names\":[\"Unity.Tutorials.dll\",\"UnityEngine.CoreModule.dll\"]}");
                File.WriteAllText(initializers, "{\"root\":[]}");
                action(directory);
            }
            finally
            {
                File.Delete(scripts);
                File.Delete(initializers);
                Directory.Delete(directory);
            }
        }
    }
}
