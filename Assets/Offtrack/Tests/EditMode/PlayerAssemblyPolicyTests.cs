using System;
using System.IO;
using System.Linq;
using Mono.Cecil;
using NUnit.Framework;
using Offtrack.Editor;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Compilation;

namespace Offtrack.Tests.EditMode
{
    public sealed class PlayerAssemblyPolicyTests
    {
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
