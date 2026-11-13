using System;
using System.Globalization;
using System.IO;
using System.Security;
using System.Text;
using UnityEditor.Build;

namespace Offtrack.Editor
{
    /// <summary>The resolved path and its original configuration, retained for actionable failures.</summary>
    public sealed class BuildRootResolution
    {
        public string ProjectRoot { get; }
        public string Root { get; }
        public string Source { get; }
        public string RawValue { get; }

        internal BuildRootResolution(string projectRoot, string root, string source, string rawValue)
        {
            ProjectRoot = projectRoot;
            Root = root;
            Source = source;
            RawValue = rawValue;
        }

        public BuildFailedException Failure(string reason)
        {
            return new BuildFailedException($"OFFTRACK build root ({Source}, raw value {Display(RawValue)}): {reason}");
        }

        internal static string Display(string value)
        {
            if (value == null) return "<null>";
            var result = new StringBuilder("\"");
            for (int i = 0; i < value.Length; i++)
            {
                char character = value[i];
                if (char.IsControl(character) || char.IsSurrogate(character) || character == '\uFFFD'
                    || CharUnicodeInfo.GetUnicodeCategory(value, i) == UnicodeCategory.Format
                    || char.IsWhiteSpace(character) && character != ' ')
                    result.Append("\\u").Append(((int)character).ToString("X4", CultureInfo.InvariantCulture));
                else result.Append(character);
            }
            return result.Append('"').ToString();
        }
    }

    /// <summary>Pure configuration selection, decoding and normalized lexical path checks.</summary>
    public static class BuildRootResolver
    {
        internal const string ArgumentName = "-offtrackBuildRoot";
        public const string SettingsFileName = "UserSettings/OfftrackBuildRoot.txt";
        private static readonly bool WindowsPaths = Path.DirectorySeparatorChar == '\\';
        private static readonly StringComparison PathComparison = WindowsPaths
            ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

        public static string Resolve(string projectRoot, string[] commandLineArgs, string settingsFileContents)
        {
            return ResolveWithSource(projectRoot, commandLineArgs, settingsFileContents).Root;
        }

        public static BuildRootResolution ResolveWithSource(string projectRoot, string[] commandLineArgs,
            string settingsFileContents)
        {
            BuildRootResolution selection = Select(projectRoot, commandLineArgs, settingsFileContents);
            try
            {
                if (!IsFullyQualified(projectRoot))
                    throw new BuildFailedException("The project root must be an absolute path.");
                projectRoot = Normalize(projectRoot);

                string selected = selection.Root;
                ValidateSyntax(selected);
                if (Path.IsPathRooted(selected) && !IsFullyQualified(selected))
                    throw new BuildFailedException("Drive-relative and root-relative export paths are ambiguous.");
                // Normalize configuration separately so inherited project-name characters are not revalidated.
                ValidateSyntax(Normalize(Path.Combine(WindowsPaths ? "C:\\" : "/", selected)));
                string root = Normalize(Path.Combine(projectRoot, selected));
                if (string.Equals(root, Normalize(Path.GetPathRoot(root)), PathComparison))
                    throw new BuildFailedException("The export root cannot be a filesystem root.");
                string builds = Normalize(Path.Combine(projectRoot, "Builds"));
                if (IsWithin(root, projectRoot) && !IsWithin(root, builds))
                    throw new BuildFailedException("An export inside the project must stay under its Builds directory.");
                if (IsWithin(projectRoot, Path.Combine(root, "OFFTRACK-Windows")))
                    throw new BuildFailedException("The export output must not contain the project itself.");
                return new BuildRootResolution(projectRoot, root, selection.Source, selection.RawValue);
            }
            catch (BuildFailedException exception)
            {
                throw selection.Failure(exception.Message);
            }
            catch (Exception exception) when (exception is ArgumentException || exception is NotSupportedException
                || exception is IOException || exception is SecurityException)
            {
                throw selection.Failure("Cannot resolve the project or configured path: " + exception.Message);
            }
        }

        public static bool HasCommandLineOverride(string[] arguments)
        {
            return SelectCommandLine(null, arguments) != null;
        }

        public static string DecodeSettings(byte[] contents)
        {
            if (contents == null) return null;
            try
            {
                // GetString deliberately avoids StreamReader's automatic UTF-16/UTF-32 BOM detection.
                return new UTF8Encoding(false, true).GetString(contents);
            }
            catch (DecoderFallbackException)
            {
                throw new BuildFailedException("OFFTRACK build root (settings file " + SettingsFileName
                    + ", raw bytes " + BitConverter.ToString(contents) + "): the file must be UTF-8 (UTF-8 BOM is allowed).");
            }
        }

        private static BuildRootResolution Select(string projectRoot, string[] arguments, string settings)
        {
            var commandLine = SelectCommandLine(projectRoot, arguments);
            if (commandLine != null) return commandLine;
            if (settings != null)
            {
                bool firstLine = true;
                using (var reader = new StringReader(settings))
                {
                    string line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        string value = Trim(line);
                        // Only the file's opening BOM is metadata; later BOMs are invalid format characters.
                        if (firstLine) value = RemoveOpeningBom(value);
                        firstLine = false;
                        if (value.Length == 0) continue;
                        return new BuildRootResolution(projectRoot, Unquote(value),
                            "settings file " + SettingsFileName, line);
                    }
                }
            }
            return new BuildRootResolution(projectRoot, "Builds", "default", "Builds");
        }

        private static BuildRootResolution SelectCommandLine(string projectRoot, string[] arguments)
        {
            BuildRootResolution selection = null;
            if (arguments == null) return null;
            for (int i = 0; i < arguments.Length; i++)
            {
                string argument = arguments[i];
                if (argument == null) continue;
                if (string.Equals(argument, ArgumentName, StringComparison.OrdinalIgnoreCase))
                {
                    bool missing = i + 1 >= arguments.Length;
                    string raw = missing ? "<missing>" : arguments[++i];
                    string value = missing ? "" : Unquote(RemoveOpeningBom(Trim(raw)));
                    var candidate = new BuildRootResolution(projectRoot, value, "command line " + argument, raw);
                    if (value.Length == 0 || value.StartsWith("-", StringComparison.Ordinal))
                        throw candidate.Failure(ArgumentName + " requires a directory value.");
                    selection = candidate;
                }
                else if (argument.StartsWith(ArgumentName, StringComparison.OrdinalIgnoreCase)
                    || argument.StartsWith("-" + ArgumentName, StringComparison.OrdinalIgnoreCase))
                {
                    throw new BuildRootResolution(projectRoot, null, "command line", argument)
                        .Failure("Unsupported build-root argument; use " + ArgumentName + " <directory>.");
                }
            }
            return selection;
        }

        private static string Trim(string value) => value == null ? "" : value.Trim();

        private static string RemoveOpeningBom(string value)
        {
            return value.Length > 0 && value[0] == '\uFEFF' ? Trim(value.Substring(1)) : value;
        }

        private static string Unquote(string value)
        {
            return value.Length >= 2 && value[0] == '"' && value[value.Length - 1] == '"'
                ? Trim(value.Substring(1, value.Length - 2)) : value;
        }

        private static string Normalize(string path)
        {
            string full = Path.GetFullPath(path);
            string root = Path.GetPathRoot(full);
            return full.Length == root.Length ? full : full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }

        private static bool IsWithin(string path, string parent)
        {
            return string.Equals(path, parent, PathComparison)
                || path.StartsWith(parent.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, PathComparison);
        }

        private static bool IsFullyQualified(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            string root = Path.GetPathRoot(path);
            if (!WindowsPaths) return root == "/";
            return root.Length == 3 && root[1] == ':' && (root[2] == '\\' || root[2] == '/')
                || root.StartsWith("\\\\", StringComparison.Ordinal)
                    && root.Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries).Length == 2;
        }

        private static void ValidateSyntax(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || path.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
                throw new BuildFailedException("The configured path is empty or contains invalid characters.");
            for (int i = 0; i < path.Length; i++)
            {
                if (path[i] == '\uFFFD')
                    throw new BuildFailedException("The path contains a replacement character (U+FFFD); check UTF-8 encoding.");
                if (CharUnicodeInfo.GetUnicodeCategory(path, i) == UnicodeCategory.Format)
                    throw new BuildFailedException("The path contains an invisible Unicode format character.");
                if (char.IsHighSurrogate(path[i]) && i + 1 < path.Length && char.IsLowSurrogate(path[i + 1])) i++;
            }
            if (!WindowsPaths) return;
            string normalized = path.Replace('/', '\\');
            if (normalized.StartsWith("\\\\?\\", StringComparison.Ordinal)
                || normalized.StartsWith("\\\\.\\", StringComparison.Ordinal))
                throw new BuildFailedException("Device namespace paths are not supported for exports.");
            for (int i = 0; i < path.Length; i++)
            {
                char character = path[i];
                if (character < ' ' || "<>\"|?*".IndexOf(character) >= 0
                    || character == ':' && (i != 1 || !IsDriveLetter(path[0])))
                    throw new BuildFailedException("The configured path contains invalid Windows characters.");
            }
            foreach (string part in normalized.Split('\\'))
            {
                if (part == "." || part == "..") continue;
                // Unicode trailing whitespace can be removed by Mono before Windows resolves dot aliases.
                string trimmed = part.TrimEnd();
                if (trimmed.EndsWith(".", StringComparison.Ordinal) || trimmed.Length != part.Length)
                    throw new BuildFailedException("Windows path components must not end in a dot or space (including Unicode whitespace).");
            }
        }

        private static bool IsDriveLetter(char character)
        {
            return character >= 'A' && character <= 'Z' || character >= 'a' && character <= 'z';
        }
    }
}
