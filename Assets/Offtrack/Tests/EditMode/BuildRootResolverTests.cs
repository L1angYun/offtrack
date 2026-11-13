using System;
using System.IO;
using System.Text;
using NUnit.Framework;
using Offtrack.Editor;
using UnityEditor.Build;

namespace Offtrack.Tests.EditMode
{
    public sealed class BuildRootResolverTests
    {
        // These paths need not exist: resolution must use only the supplied inputs.
        private static readonly string TestRoot = Path.Combine(Path.GetPathRoot(Path.GetFullPath(".")),
            "OfftrackBuildRootTests");
        private static readonly string ProjectRoot = Path.Combine(TestRoot, "Project");
        private static readonly string ExternalRoot = Path.Combine(TestRoot, "Exports");

        [TestCase(null)]
        [TestCase("")]
        [TestCase("\uFEFF \t\r\n \t\n")]
        public void MissingOrEmptySettingsUseTheProjectsBuildsDirectory(string settings)
        {
            Assert.That(BuildRootResolver.Resolve(ProjectRoot, Array.Empty<string>(), settings),
                Is.EqualTo(Path.Combine(ProjectRoot, "Builds")));
        }

        [Test]
        public void NullCommandLineUsesTheDefault()
        {
            Assert.That(BuildRootResolver.Resolve(ProjectRoot, null, null),
                Is.EqualTo(Path.Combine(ProjectRoot, "Builds")));
        }

        [TestCase("\u200C")]
        [TestCase("\uFFFD")]
        [TestCase("\u200D")]
        public void DefaultBuildsPreservesCharactersInheritedFromTheProjectPath(string character)
        {
            string project = Path.Combine(TestRoot, "Project" + character + "Name");
            Assert.That(BuildRootResolver.Resolve(project, Array.Empty<string>(), null),
                Is.EqualTo(Path.Combine(project, "Builds")));
        }

        [TestCase("\u200C")]
        [TestCase("\uFFFD")]
        public void RelativeConfigurationDoesNotValidateTheInheritedProjectPrefix(string character)
        {
            string project = Path.Combine(TestRoot, "Project" + character + "Name");
            Assert.That(BuildRootResolver.Resolve(project, Array.Empty<string>(), "Builds/Release"),
                Is.EqualTo(Path.Combine(project, "Builds", "Release")));
        }

        [TestCase("\u200C")]
        [TestCase("\uFFFD")]
        public void DefaultBuildsDoesNotValidateInheritedUncShareCharacters(string character)
        {
            if (Path.DirectorySeparatorChar != '\\') Assert.Ignore("Windows UNC paths.");
            string project = "\\\\server\\share" + character + "\\Project";
            Assert.That(BuildRootResolver.Resolve(project, Array.Empty<string>(), null),
                Is.EqualTo(Path.Combine(project, "Builds")));
        }

        [Test]
        public void UnrelatedCommandLineOptionsDoNotOverrideSettings()
        {
            Assert.That(BuildRootResolver.Resolve(ProjectRoot,
                new[] { "Unity.exe", "-batchmode", "--burst-disable-compilation" }, ExternalRoot),
                Is.EqualTo(ExternalRoot));
        }

        [Test]
        public void SettingsCanSelectAnAbsoluteExternalDirectory()
        {
            Assert.That(BuildRootResolver.Resolve(ProjectRoot, Array.Empty<string>(), ExternalRoot),
                Is.EqualTo(ExternalRoot));
        }

        [Test]
        public void OnlyTheFirstNonemptySettingsLineIsUsed()
        {
            string settings = "\r\n \t\n" + ExternalRoot + "\r\nAssets\n";
            Assert.That(BuildRootResolver.Resolve(ProjectRoot, Array.Empty<string>(), settings),
                Is.EqualTo(ExternalRoot));
        }

        [TestCase("\uFEFF  ", " \t")]
        [TestCase(" \t", " \u00A0 \t")]
        [TestCase("\uFEFF\r\n \t\n", "\r\nAssets")]
        public void SettingsTrimWhitespaceAndBomBeforeSelectingTheFirstNonemptyLine(string prefix, string suffix)
        {
            Assert.That(BuildRootResolver.Resolve(ProjectRoot, Array.Empty<string>(), prefix + ExternalRoot + suffix),
                Is.EqualTo(ExternalRoot));
        }

        [TestCase("Builds/FromSettings")]
        [TestCase("Assets")]
        [TestCase("invalid\0path")]
        public void CommandLineTakesPriorityEvenWhenSettingsWouldBeRejected(string settings)
        {
            Assert.That(BuildRootResolver.Resolve(ProjectRoot,
                new[] { "Unity.exe", "-batchmode", "-offtrackBuildRoot", ExternalRoot, "-quit" }, settings),
                Is.EqualTo(ExternalRoot));
        }

        [Test]
        public void CommandLineValuesTrimWhitespaceAndBom()
        {
            string value = "\uFEFF \t" + ExternalRoot + " \u00A0 \t";
            Assert.That(BuildRootResolver.Resolve(ProjectRoot, new[] { "-offtrackBuildRoot", value }, null),
                Is.EqualTo(ExternalRoot));
        }

        [TestCase(true)]
        [TestCase(false)]
        public void RelativePathsResolveAgainstTheSuppliedProjectRoot(bool commandLine)
        {
            const string relative = "Builds/Staging/../Release";
            string result = BuildRootResolver.Resolve(ProjectRoot,
                commandLine ? new[] { "-offtrackBuildRoot", relative } : Array.Empty<string>(),
                commandLine ? null : relative);
            Assert.That(result, Is.EqualTo(Path.Combine(ProjectRoot, "Builds", "Release")));
        }

        [TestCase(true)]
        [TestCase(false)]
        public void RelativePathsMaySelectAnExternalSiblingDirectory(bool commandLine)
        {
            const string relative = "../Exports";
            string result = BuildRootResolver.Resolve(ProjectRoot,
                commandLine ? new[] { "-offtrackBuildRoot", relative } : Array.Empty<string>(),
                commandLine ? null : relative);
            Assert.That(result, Is.EqualTo(ExternalRoot));
        }

        [TestCase("Builds")]
        [TestCase("Builds/")]
        [TestCase("./Builds/./")]
        public void TheBuildsDirectoryItselfIsAllowedAndNormalized(string root)
        {
            Assert.That(BuildRootResolver.Resolve(ProjectRoot, new[] { "-offtrackBuildRoot", root }, null),
                Is.EqualTo(Path.Combine(ProjectRoot, "Builds")));
        }

        [Test]
        public void TrailingSeparatorsOnTheProjectAndSelectedRootAreNormalized()
        {
            Assert.That(BuildRootResolver.Resolve(ProjectRoot + Path.DirectorySeparatorChar,
                new[] { "-offtrackBuildRoot", ExternalRoot + Path.DirectorySeparatorChar }, null),
                Is.EqualTo(ExternalRoot));
        }

        [Test]
        public void ProjectNamePrefixesDoNotMakeAnExternalDirectoryPartOfTheProject()
        {
            string sibling = ProjectRoot + "-exports";
            Assert.That(BuildRootResolver.Resolve(ProjectRoot, new[] { "-offtrackBuildRoot", sibling }, null),
                Is.EqualTo(sibling));
        }

        [Test]
        public void WindowsBuildsBoundaryIgnoresCase()
        {
            if (Path.DirectorySeparatorChar != '\\') Assert.Ignore("Windows path comparison.");
            string root = Path.Combine(ProjectRoot.ToUpperInvariant(), "bUiLdS", "Release");
            Assert.That(BuildRootResolver.Resolve(ProjectRoot, new[] { "-offtrackBuildRoot", root }, null),
                Is.EqualTo(Path.Combine(ProjectRoot, "Builds", "Release")).IgnoreCase);
        }

        [TestCase(".")]
        [TestCase("Assets")]
        [TestCase("Library")]
        [TestCase("ProjectSettings")]
        [TestCase("Packages")]
        [TestCase("UserSettings")]
        [TestCase("Temp")]
        [TestCase("Logs")]
        [TestCase("Builds2")]
        [TestCase("Builds-backup/Release")]
        [TestCase("Builds/../Assets")]
        [TestCase("Builds/..")]
        public void ProjectDirectoriesOutsideBuildsAreRejected(string root)
        {
            AssertFailure(() => BuildRootResolver.Resolve(ProjectRoot,
                new[] { "-offtrackBuildRoot", root }, null), "command line", root, "Builds directory");
        }

        [Test]
        public void AnAbsoluteProjectRootIsRejected()
        {
            AssertFailure(() => BuildRootResolver.Resolve(ProjectRoot,
                new[] { "-offtrackBuildRoot", ProjectRoot }, null), "command line", ProjectRoot, "Builds directory");
        }

        [TestCase("")]
        [TestCase("NestedProject")]
        public void AnExternalRootCannotMakeTheOutputContainTheProject(string child)
        {
            string project = Path.Combine(TestRoot, "OFFTRACK-Windows", child);
            AssertFailure(() => BuildRootResolver.Resolve(project,
                new[] { "-offtrackBuildRoot", TestRoot }, null), "command line", TestRoot, "contain the project");
        }

        [Test]
        public void UnsafeSettingsFailInsteadOfFallingBackToBuilds()
        {
            string raw = Path.Combine(ProjectRoot, "Assets");
            AssertFailure(() => BuildRootResolver.Resolve(ProjectRoot,
                Array.Empty<string>(), raw), "settings file", raw, "UserSettings/OfftrackBuildRoot.txt", "Builds directory");
        }

        [Test]
        public void WindowsCaseChangesCannotBypassTheProjectBoundary()
        {
            if (Path.DirectorySeparatorChar != '\\') Assert.Ignore("Windows path comparison.");
            string root = Path.Combine(ProjectRoot.ToUpperInvariant(), "aSsEtS");
            AssertFailure(() => BuildRootResolver.Resolve(ProjectRoot,
                new[] { "-offtrackBuildRoot", root }, null), "command line", root, "Builds directory");
        }

        [TestCase(true)]
        [TestCase(false)]
        public void FileSystemRootsAreRejectedFromEitherSource(bool commandLine)
        {
            string root = Path.GetPathRoot(ProjectRoot);
            AssertFailure(() => BuildRootResolver.Resolve(ProjectRoot,
                commandLine ? new[] { "-offtrackBuildRoot", root } : Array.Empty<string>(),
                commandLine ? null : root), commandLine ? "command line" : "settings file", root, "filesystem root");
        }

        [Test]
        public void NormalizingParentSegmentsCannotSelectTheFileSystemRoot()
        {
            AssertFailure(() => BuildRootResolver.Resolve(ProjectRoot,
                new[] { "-offtrackBuildRoot", "../.." }, null), "command line", "../..", "filesystem root");
        }

        [Test]
        public void ACommandLineFlagWithoutAFollowingArgumentIsRejected()
        {
            AssertFailure(() => BuildRootResolver.Resolve(ProjectRoot,
                new[] { "Unity.exe", "-offtrackBuildRoot" }, ExternalRoot), "command line", "-offtrackBuildRoot",
                "requires a directory value");
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase(" \t")]
        [TestCase("\uFEFF")]
        [TestCase("-batchmode")]
        [TestCase("--burst-disable-compilation")]
        public void MissingCommandLineValuesNeverFallBackToSettings(string value)
        {
            AssertFailure(() => BuildRootResolver.Resolve(ProjectRoot,
                new[] { "-offtrackBuildRoot", value }, ExternalRoot), "command line", "-offtrackBuildRoot",
                "requires a directory value");
        }

        [TestCase(true)]
        [TestCase(false)]
        public void UnresolvablePathsAreReportedAsBuildFailures(bool commandLine)
        {
            const string invalid = "Builds/invalid\0path";
            AssertFailure(() => BuildRootResolver.Resolve(ProjectRoot,
                commandLine ? new[] { "-offtrackBuildRoot", invalid } : Array.Empty<string>(),
                commandLine ? null : invalid), commandLine ? "command line" : "settings file", "invalid", "characters");
        }

        [Test]
        public void WindowsDriveRelativePathsCannotDependOnTheCurrentDirectoryOfADrive()
        {
            if (Path.DirectorySeparatorChar != '\\') Assert.Ignore("Windows path syntax.");
            string root = Path.GetPathRoot(ProjectRoot).Substring(0, 2) + "relative";
            AssertFailure(() => BuildRootResolver.Resolve(ProjectRoot,
                new[] { "-offtrackBuildRoot", root }, null), "command line", root, "relative", "ambiguous");
        }

        [Test]
        public void WindowsRootRelativePathsCannotDependOnTheCurrentDrive()
        {
            if (Path.DirectorySeparatorChar != '\\') Assert.Ignore("Windows path syntax.");
            AssertFailure(() => BuildRootResolver.Resolve(ProjectRoot,
                new[] { "-offtrackBuildRoot", "\\relative" }, null), "command line", "\\relative", "relative", "ambiguous");
        }

        [TestCase("Builds/release*")]
        [TestCase("Builds/release?")]
        [TestCase("Builds/invalid\"name")]
        [TestCase("Builds/name:stream")]
        [TestCase("Builds/invalid<name")]
        [TestCase("Builds/invalid|name")]
        public void WindowsInvalidDirectoryCharactersAreRejected(string root)
        {
            if (Path.DirectorySeparatorChar != '\\') Assert.Ignore("Windows path syntax.");
            AssertFailure(() => BuildRootResolver.Resolve(ProjectRoot,
                new[] { "-offtrackBuildRoot", root }, null), "command line", root, "characters");
        }

        [TestCase("\\\\?\\C:\\Exports")]
        [TestCase("\\\\.\\C:\\Exports")]
        public void WindowsDevicePathsCannotBypassNormalPathValidation(string root)
        {
            if (Path.DirectorySeparatorChar != '\\') Assert.Ignore("Windows path syntax.");
            AssertFailure(() => BuildRootResolver.Resolve(ProjectRoot,
                new[] { "-offtrackBuildRoot", root }, null), "command line", root, "Device namespace");
        }

        [TestCase("\\\\server\\share\\")]
        [TestCase("\\\\server\\share")]
        public void WindowsUncShareRootsAreRejected(string root)
        {
            if (Path.DirectorySeparatorChar != '\\') Assert.Ignore("Windows path syntax.");
            AssertFailure(() => BuildRootResolver.Resolve(ProjectRoot,
                new[] { "-offtrackBuildRoot", root }, null), "command line", root, "filesystem root");
        }

        [Test]
        public void WindowsUncShareSubdirectoriesResolveWithoutAccessingTheShare()
        {
            if (Path.DirectorySeparatorChar != '\\') Assert.Ignore("Windows path syntax.");
            const string root = "\\\\server\\share\\Exports";
            Assert.That(BuildRootResolver.Resolve(ProjectRoot, new[] { "-offtrackBuildRoot", root }, null),
                Is.EqualTo(root));
        }

        [TestCase("Builds/../Assets.")]
        [TestCase("Builds/Release.")]
        [TestCase("Builds/Release./Nested")]
        [TestCase("Builds/Release /Nested")]
        [TestCase("Builds/../Assets /Nested")]
        public void WindowsTrailingDotsAndSpacesCannotCreateDirectoryAliases(string root)
        {
            if (Path.DirectorySeparatorChar != '\\') Assert.Ignore("Windows path syntax.");
            AssertFailure(() => BuildRootResolver.Resolve(ProjectRoot,
                new[] { "-offtrackBuildRoot", root }, null), "command line", root, "dot or space");
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("invalid\0project")]
        public void InvalidProjectRootsAreReportedAsBuildFailures(string project)
        {
            AssertFailure(() => BuildRootResolver.Resolve(project,
                Array.Empty<string>(), null), "default", "Builds", "project");
        }

        [TestCase("-offtrackbuildroot")]
        [TestCase("-OFFTRACKBUILDROOT")]
        [TestCase("-OfftrackBuildRoot")]
        public void CommandLineArgumentNamesAreCaseInsensitive(string argument)
        {
            string[] arguments = { argument, ExternalRoot };
            Assert.That(BuildRootResolver.HasCommandLineOverride(arguments), Is.True);
            Assert.That(BuildRootResolver.Resolve(ProjectRoot, arguments, "Assets"), Is.EqualTo(ExternalRoot));
        }

        [TestCase("--offtrackBuildRoot")]
        [TestCase("-offtrackBuildRoot=Builds/Release")]
        [TestCase("--OFFTRACKBUILDROOT=Builds/Release")]
        [TestCase("-offtrackbuildrootExtra")]
        [TestCase("--offtrackbuildrootExtra")]
        [TestCase("-OFFTRACKBUILDROOT:Builds/Release")]
        public void SimilarButUnsupportedArgumentsFailBeforeFallingBackToSettings(string argument)
        {
            string[] arguments = { "Unity.exe", argument, ExternalRoot };
            AssertFailure(() => BuildRootResolver.Resolve(ProjectRoot, arguments, ExternalRoot),
                "command line", argument, "unsupported", "-offtrackBuildRoot");
            AssertFailure(() => BuildRootResolver.HasCommandLineOverride(arguments),
                "command line", argument, "unsupported", "-offtrackBuildRoot");
        }

        [Test]
        public void AValidOverrideDoesNotHideALaterMisspelledOverride()
        {
            string[] arguments = { "-offtrackBuildRoot", ExternalRoot, "--offtrackBuildRoot", "Builds/Other" };
            AssertFailure(() => BuildRootResolver.Resolve(ProjectRoot, arguments, null),
                "command line", "--offtrackBuildRoot", "unsupported");
            AssertFailure(() => BuildRootResolver.HasCommandLineOverride(arguments),
                "command line", "--offtrackBuildRoot", "unsupported");
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("\u00A0\u2003")]
        [TestCase("-quit")]
        public void OverrideDetectionDoesNotPermitMissingValues(string value)
        {
            AssertFailure(() => BuildRootResolver.HasCommandLineOverride(new[] { "-OFFTRACKBUILDROOT", value }),
                "command line", "-OFFTRACKBUILDROOT", "requires a directory value");
        }

        [Test]
        public void UnrelatedOptionsDoNotCountAsABuildRootOverride()
        {
            Assert.That(BuildRootResolver.HasCommandLineOverride(new[] { "-batchmode", "-projectPath", ProjectRoot }), Is.False);
            Assert.That(BuildRootResolver.HasCommandLineOverride(null), Is.False);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void StrictUtf8DecodingAcceptsChinesePathsWithOrWithoutABom(bool bom)
        {
            const string contents = "Builds/中文导出";
            byte[] bytes = Encoding.UTF8.GetBytes((bom ? "\uFEFF" : "") + contents);
            string settings = BuildRootResolver.DecodeSettings(bytes);
            Assert.That(BuildRootResolver.Resolve(ProjectRoot, Array.Empty<string>(), settings),
                Is.EqualTo(Path.Combine(ProjectRoot, "Builds", "中文导出")));
        }

        [TestCase("CP936", new byte[] { 0xD6, 0xD0, 0xCE, 0xC4 })]
        [TestCase("UTF16LE", new byte[] { 0xFF, 0xFE, 0x42, 0x00 })]
        [TestCase("UTF16BE", new byte[] { 0xFE, 0xFF, 0x00, 0x42 })]
        [TestCase("TruncatedUTF8", new byte[] { 0xE4, 0xB8 })]
        public void NonUtf8SettingsNameTheFileAndRequiredEncoding(string encoding, byte[] bytes)
        {
            var exception = AssertFailure(() => BuildRootResolver.DecodeSettings(bytes),
                "settings file", "UserSettings/OfftrackBuildRoot.txt", "UTF-8");
            Assert.That(exception.Message, Does.Contain(BitConverter.ToString(bytes)),
                "The original undecodable bytes must remain diagnosable: " + encoding);
        }

        [TestCase(true)]
        [TestCase(false)]
        public void ReplacementCharactersAreRejectedEvenInValidUtf8(bool commandLine)
        {
            const string root = "Builds/bad\uFFFDpath";
            AssertFailure(() => BuildRootResolver.Resolve(ProjectRoot,
                commandLine ? new[] { "-offtrackBuildRoot", root } : Array.Empty<string>(),
                commandLine ? null : root), commandLine ? "command line" : "settings file",
                "\\uFFFD", "replacement character");
        }

        [TestCase("\u200B", "\\u200B")]
        [TestCase("\u200E", "\\u200E")]
        [TestCase("\u2060", "\\u2060")]
        [TestCase("\u202E", "\\u202E")]
        [TestCase("\uFEFF", "\\uFEFF")]
        [TestCase("\U000E0001", "\\uDB40\\uDC01")]
        public void InvisibleFormatCharactersInsideAPathAreRejected(string character, string escaped)
        {
            string root = "Builds/release" + character + "copy";
            AssertFailure(() => BuildRootResolver.Resolve(ProjectRoot,
                new[] { "-offtrackBuildRoot", root }, null), "command line", escaped, "format character");
            AssertFailure(() => BuildRootResolver.Resolve(ProjectRoot, Array.Empty<string>(), root),
                "settings file", escaped, "format character");
        }

        [TestCase("Builds/Release\uFEFF")]
        [TestCase("Builds/Release\uFEFF \t")]
        public void TrailingBomIsNotSilentlyTrimmed(string root)
        {
            AssertFailure(() => BuildRootResolver.Resolve(ProjectRoot, Array.Empty<string>(), root),
                "settings file", "\\uFEFF", "format character");
        }

        [TestCase("\u00A0")]
        [TestCase("\u0085")]
        [TestCase("\u2003")]
        [TestCase("\u2028\u2029")]
        [TestCase("\u202F\u205F\u3000")]
        public void AllUnicodeWhitespaceAtThePathEdgesIsTrimmed(string whitespace)
        {
            string root = whitespace + "Builds/Release" + whitespace;
            Assert.That(BuildRootResolver.Resolve(ProjectRoot, new[] { "-offtrackBuildRoot", root }, null),
                Is.EqualTo(Path.Combine(ProjectRoot, "Builds", "Release")));
            Assert.That(BuildRootResolver.Resolve(ProjectRoot, Array.Empty<string>(), root),
                Is.EqualTo(Path.Combine(ProjectRoot, "Builds", "Release")));
        }

        [TestCase(".\u00A0")]
        [TestCase(".\u2003")]
        [TestCase(".\u3000")]
        public void NormalizationCannotTurnUnicodeWhitespaceIntoAProjectAlias(string suffix)
        {
            if (Path.DirectorySeparatorChar != '\\') Assert.Ignore("Windows path normalization.");
            string root = ProjectRoot + suffix + Path.DirectorySeparatorChar + "Assets";
            AssertFailure(() => BuildRootResolver.Resolve(ProjectRoot, new[] { "-offtrackBuildRoot", root }, null),
                "command line", "Assets", "dot or space");
        }

        [TestCase("\"Builds/Release\"")]
        [TestCase(" \t\"Builds/Release\"\u00A0")]
        [TestCase("\uFEFF\"Builds/Release\"")]
        public void SettingsAcceptOnePairOfOuterDoubleQuotes(string root)
        {
            Assert.That(BuildRootResolver.Resolve(ProjectRoot, Array.Empty<string>(), root),
                Is.EqualTo(Path.Combine(ProjectRoot, "Builds", "Release")));
        }

        [Test]
        public void QuotedAbsolutePathsCanContainSpacesAndChineseCharacters()
        {
            string root = Path.Combine(TestRoot, "我的导出", "Release Candidate");
            Assert.That(BuildRootResolver.Resolve(ProjectRoot, Array.Empty<string>(), "\"" + root + "\""), Is.EqualTo(root));
        }

        [TestCase("\"Builds/Release")]
        [TestCase("Builds/Release\"")]
        [TestCase("\"\"Builds/Release\"\"")]
        public void UnpairedOrNestedQuotesAreRejected(string root)
        {
            if (Path.DirectorySeparatorChar != '\\') Assert.Ignore("Windows path syntax.");
            AssertFailure(() => BuildRootResolver.Resolve(ProjectRoot, Array.Empty<string>(), root),
                "settings file", "Builds/Release", "invalid");
        }

        [Test]
        public void ResolutionRetainsTheOriginalSelectedValueForLaterFilesystemErrors()
        {
            const string raw = "  \"Builds/Release\" \u00A0";
            var resolution = BuildRootResolver.ResolveWithSource(ProjectRoot, Array.Empty<string>(), raw);
            Assert.That(resolution.Root, Is.EqualTo(Path.Combine(ProjectRoot, "Builds", "Release")));
            Assert.That(resolution.ProjectRoot, Is.EqualTo(ProjectRoot));
            Assert.That(resolution.Source, Does.Contain("settings file"));
            Assert.That(resolution.RawValue, Is.EqualTo(raw));
            AssertFailure(() => throw resolution.Failure("archive write failed"),
                "settings file", "Builds/Release", "archive write failed");
        }

        [Test]
        public void DefaultResolutionReportsTheDefaultValueOnLaterFailure()
        {
            var resolution = BuildRootResolver.ResolveWithSource(ProjectRoot, Array.Empty<string>(), null);
            Assert.That(resolution.Root, Is.EqualTo(Path.Combine(ProjectRoot, "Builds")));
            Assert.That(resolution.Source, Does.Contain("default"));
            Assert.That(resolution.RawValue, Is.EqualTo("Builds"));
            AssertFailure(() => throw resolution.Failure("archive write failed"),
                "default", "Builds", "archive write failed");
        }

        private static BuildFailedException AssertFailure(TestDelegate operation, string source,
            string rawFragment, params string[] reasons)
        {
            var exception = Assert.Throws<BuildFailedException>(operation);
            Assert.That(exception.Message, Does.Contain(source).IgnoreCase, "The error must identify its source.");
            Assert.That(exception.Message, Does.Contain(rawFragment), "The error must preserve the supplied raw value.");
            foreach (string reason in reasons)
                Assert.That(exception.Message, Does.Contain(reason).IgnoreCase, "The error must explain the actual rejection.");
            return exception;
        }
    }
}
