using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using NUnit.Framework;
using Offtrack.Editor;
using UnityEditor.Build;

namespace Offtrack.Tests.EditMode
{
    public sealed class ExportOutputTests
    {
        private string sandbox;
        private BuildRootResolution resolution;
        private readonly List<string> junctions = new List<string>();
        private const string RawRoot = "  \"../Exports\"  ";

        [SetUp]
        public void SetUp()
        {
            sandbox = Path.Combine(Path.GetTempPath(), "OFFTRACK-E2-Output-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(sandbox);
            resolution = BuildRootResolver.ResolveWithSource(Path.Combine(sandbox, "Project"),
                Array.Empty<string>(), RawRoot);
            Directory.CreateDirectory(resolution.Root);
        }

        [TearDown]
        public void TearDown()
        {
            // Link targets remain alive until every owned junction entry has been removed non-recursively.
            foreach (string link in junctions)
            {
                AssertOwned(link);
                if (Directory.Exists(link)) Directory.Delete(link, false);
            }
            junctions.Clear();
            if (sandbox == null || !Directory.Exists(sandbox)) return;
            string temporaryRoot = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar)
                + Path.DirectorySeparatorChar;
            Assert.That(Path.GetFullPath(sandbox).StartsWith(temporaryRoot, StringComparison.OrdinalIgnoreCase), Is.True);
            Assert.That(Path.GetFileName(sandbox), Does.StartWith("OFFTRACK-E2-Output-"));
            Directory.Delete(sandbox, true);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void CleanRemovesJunctionEntriesWithoutDeletingTheirTargets(bool nested)
        {
            RequireWindowsLinks();
            string target = Directory.CreateDirectory(Path.Combine(sandbox, "Target")).FullName;
            string sentinel = Path.Combine(target, "keep.txt");
            File.WriteAllText(sentinel, "keep this target content");
            string output = Path.Combine(resolution.Root, "OFFTRACK-Windows");
            if (nested) Directory.CreateDirectory(output);
            string link = nested ? Path.Combine(output, "LinkedContent") : output;
            CreateLink("/J", link, target);
            junctions.Add(link);

            ExportOutput.Clean(output, resolution);

            Assert.That(Directory.Exists(output), Is.False);
            Assert.That(Directory.Exists(link), Is.False);
            Assert.That(File.ReadAllText(sentinel), Is.EqualTo("keep this target content"));
            TestContext.WriteLine("Unity Mono Clean: " + (nested ? "nested junction" : "output junction")
                + " removed; target content preserved.");
        }

        [Test]
        public void NormalOutputCanBeCreatedThenCleanedWithoutAPlatformGuard()
        {
            string output = Path.Combine(resolution.Root, "NewRoot", "OFFTRACK-Windows");
            ExportOutput.CreateDirectory(output, resolution);
            File.WriteAllText(Path.Combine(output, "old.txt"), "old");
            ExportOutput.Clean(output, resolution);
            Assert.That(Directory.Exists(output), Is.False);
            Assert.DoesNotThrow(() => ExportOutput.Clean(output, resolution));
        }

        [TestCase(true)]
        [TestCase(false)]
        public void DeletingAnArchiveInAMissingRootDoesNotCreateTheRoot(bool useDefault)
        {
            var fresh = FreshResolution(useDefault);
            string archive = Path.Combine(fresh.Root, "OFFTRACK-Windows.zip");
            Assert.That(Directory.Exists(fresh.Root), Is.False, "Do not let SetUp hide the missing-root regression.");

            Assert.DoesNotThrow(() => ExportOutput.DeleteFile(archive, fresh));

            Assert.That(Directory.Exists(fresh.Root), Is.False, "Deleting a missing file must have no creation side effect.");
            Assert.That(File.Exists(archive), Is.False);
        }

        [TestCase(true)]
        [TestCase(false)]
        public void FirstExportFileSequenceCreatesAMissingRootAndPublishesTheArchive(bool useDefault)
        {
            var fresh = FreshResolution(useDefault);
            string archive = Path.Combine(fresh.Root, "OFFTRACK-Windows.zip");
            string output = Path.Combine(fresh.Root, "OFFTRACK-Windows");
            string partial = archive + ".partial";
            byte[] contents = Encoding.UTF8.GetBytes("archive content for a fresh root");
            Assert.That(Directory.Exists(fresh.Root), Is.False, "The complete sequence must start without a root directory.");

            ExportOutput.DeleteFile(archive, fresh);
            ExportOutput.Clean(output, fresh);
            ExportOutput.CreateDirectory(output, fresh);
            using (FileStream stream = ExportOutput.CreatePartialArchive(partial, fresh))
                stream.Write(contents, 0, contents.Length);
            ExportOutput.Move(partial, archive, fresh);

            Assert.That(Directory.Exists(fresh.Root), Is.True);
            Assert.That(Directory.Exists(output), Is.True);
            Assert.That(File.ReadAllBytes(archive), Is.EqualTo(contents));
            Assert.That(File.Exists(partial), Is.False);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void PartialCleanupAllowsAMissingParentWithoutCreatingIt(bool exportFailed)
        {
            var fresh = FreshResolution(false);
            string partial = Path.Combine(fresh.Root, "OFFTRACK-Windows.zip.partial");
            Assert.That(Directory.Exists(fresh.Root), Is.False);

            Assert.DoesNotThrow(() => ExportOutput.CleanupPartial(partial, fresh, exportFailed));

            Assert.That(Directory.Exists(fresh.Root), Is.False);
        }

        [Test]
        public void PartialCleanupCannotReplaceTheOriginalExportException()
        {
            RequireWindowsLinks();
            string partial = Path.Combine(resolution.Root, "OFFTRACK-Windows.zip.partial");
            File.WriteAllText(partial, "locked partial");
            var original = new InvalidOperationException("original archive write failure");
            using (File.Open(partial, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                var actual = Assert.Throws<InvalidOperationException>(() =>
                {
                    bool exportFailed = false;
                    try { throw original; }
                    catch { exportFailed = true; throw; }
                    finally { ExportOutput.CleanupPartial(partial, resolution, exportFailed); }
                });
                Assert.That(actual, Is.SameAs(original), "Cleanup must preserve the original exception, not just its type.");
            }
            Assert.That(File.ReadAllText(partial), Is.EqualTo("locked partial"));
        }

        [Test]
        public void PartialCleanupWithoutAnOriginalFailureStillReportsDeletionFailure()
        {
            RequireWindowsLinks();
            string partial = Path.Combine(resolution.Root, "OFFTRACK-Windows.zip.partial");
            File.WriteAllText(partial, "locked partial");
            using (File.Open(partial, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                AssertDiagnostic(() => ExportOutput.CleanupPartial(partial, resolution, false), partial);
            Assert.That(File.ReadAllText(partial), Is.EqualTo("locked partial"));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void PartialCleanupRemovesAnExistingUnlockedFile(bool exportFailed)
        {
            string partial = Path.Combine(resolution.Root, "OFFTRACK-Windows.zip.partial");
            File.WriteAllText(partial, "unfinished archive");

            ExportOutput.CleanupPartial(partial, resolution, exportFailed);

            Assert.That(File.Exists(partial), Is.False);
        }

        [Test]
        public void LockedZipDeletionReportsTheSettingsSourceRawValueAndPath()
        {
            RequireWindowsLinks();
            string path = Path.Combine(resolution.Root, "OFFTRACK-Windows.zip");
            File.WriteAllText(path, "old archive");
            using (File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                AssertDiagnostic(() => ExportOutput.DeleteFile(path, resolution), path);
            Assert.That(File.ReadAllText(path), Is.EqualTo("old archive"));
        }

        [Test]
        public void LockedOutputCleanupReportsTheSettingsSourceRawValueAndPath()
        {
            RequireWindowsLinks();
            string output = Directory.CreateDirectory(Path.Combine(resolution.Root, "OFFTRACK-Windows")).FullName;
            string file = Path.Combine(output, "locked.bin");
            File.WriteAllText(file, "old output");
            using (File.Open(file, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                AssertDiagnostic(() => ExportOutput.Clean(output, resolution), output);
            Assert.That(File.ReadAllText(file), Is.EqualTo("old output"));
        }

        [Test]
        public void DirectoryCreationFailureReportsTheSettingsSourceRawValueAndPath()
        {
            string path = Path.Combine(resolution.Root, "OFFTRACK-Windows");
            File.WriteAllText(path, "a file occupies the output path");
            AssertDiagnostic(() => ExportOutput.CreateDirectory(path, resolution), path);
            Assert.That(File.ReadAllText(path), Is.EqualTo("a file occupies the output path"));
        }

        [Test]
        public void LockedPartialCreationReportsTheSettingsSourceRawValueAndPath()
        {
            RequireWindowsLinks();
            string path = Path.Combine(resolution.Root, "OFFTRACK-Windows.zip.partial");
            File.WriteAllText(path, "old partial");
            using (File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                AssertDiagnostic(() => { using (ExportOutput.CreatePartialArchive(path, resolution)) { } }, path);
            Assert.That(File.ReadAllText(path), Is.EqualTo("old partial"));
        }

        [Test]
        public void FailedMoveReportsBothPathsAndKeepsTheExistingArchive()
        {
            string source = Path.Combine(resolution.Root, "OFFTRACK-Windows.zip.partial");
            string destination = Path.Combine(resolution.Root, "OFFTRACK-Windows.zip");
            File.WriteAllText(source, "new archive");
            File.WriteAllText(destination, "old archive");
            var exception = AssertDiagnostic(() => ExportOutput.Move(source, destination, resolution), destination);
            Assert.That(exception.Message, Does.Contain(source));
            Assert.That(File.ReadAllText(source), Is.EqualTo("new archive"));
            Assert.That(File.ReadAllText(destination), Is.EqualTo("old archive"));
        }

        [Test]
        public void PartialHardLinkIsUnlinkedBeforeWritingAndPublishingANewArchive()
        {
            RequireWindowsLinks();
            string source = Path.Combine(sandbox, "Original.bin");
            File.WriteAllText(source, "original content");
            string partial = Path.Combine(resolution.Root, "OFFTRACK-Windows.zip.partial");
            string archive = Path.Combine(resolution.Root, "OFFTRACK-Windows.zip");
            CreateLink("/H", partial, source);
            using (FileStream stream = ExportOutput.CreatePartialArchive(partial, resolution))
            {
                byte[] bytes = Encoding.UTF8.GetBytes("new archive");
                stream.Write(bytes, 0, bytes.Length);
            }
            ExportOutput.Move(partial, archive, resolution);
            Assert.That(File.Exists(partial), Is.False);
            Assert.That(File.ReadAllText(source), Is.EqualTo("original content"));
            Assert.That(File.ReadAllText(archive), Is.EqualTo("new archive"));
            ExportOutput.DeleteFile(archive, resolution);
            Assert.That(File.Exists(archive), Is.False);
        }

        private BuildFailedException AssertDiagnostic(TestDelegate operation, string path)
        {
            var exception = Assert.Throws<BuildFailedException>(operation);
            Assert.That(exception.Message, Does.Contain("settings file"));
            Assert.That(exception.Message, Does.Contain("UserSettings/OfftrackBuildRoot.txt"));
            Assert.That(exception.Message, Does.Contain(RawRoot));
            Assert.That(exception.Message, Does.Contain(path));
            return exception;
        }

        private BuildRootResolution FreshResolution(bool useDefault)
        {
            var fresh = BuildRootResolver.ResolveWithSource(Path.Combine(sandbox, "FreshProject"),
                Array.Empty<string>(), useDefault ? null : "../NewExports/NestedRoot");
            AssertOwned(fresh.Root);
            return fresh;
        }

        private static void RequireWindowsLinks()
        {
            if (Path.DirectorySeparatorChar != '\\') Assert.Ignore("Requires Windows junctions or Windows file-sharing locks.");
        }

        private void AssertOwned(string path)
        {
            string prefix = Path.GetFullPath(sandbox).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            Assert.That(Path.GetFullPath(path).StartsWith(prefix, StringComparison.OrdinalIgnoreCase), Is.True,
                "A test path must stay inside its owned temporary directory.");
        }

        private void CreateLink(string kind, string link, string target)
        {
            AssertOwned(link);
            AssertOwned(target);
            var start = new ProcessStartInfo("cmd.exe", "/d /c mklink " + kind + " \"" + link + "\" \"" + target + "\"")
            {
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true
            };
            using (var process = Process.Start(start))
            {
                string output = process.StandardOutput.ReadToEnd();
                string error = process.StandardError.ReadToEnd();
                process.WaitForExit();
                Assert.That(process.ExitCode, Is.EqualTo(0), "Could not create owned test link: " + output + error);
            }
        }
    }
}
