using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Mono.Cecil;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace Offtrack.Editor
{
    /// <summary>Filter compiled Player assemblies and reconcile exported initializer metadata.</summary>
    public static class PlayerAssemblyPolicy
    {
        // com.unity.collections/Unity.Collections.Tests/CoreCLR: nested-job visibility fixtures.
        // Their asmdefs lack UNITY_INCLUDE_TESTS. They have no incoming Player references;
        // the reference check below fails closed if a future package introduces one.
        // This is an exclusion list, NOT a whitelist permitting tests to ship.
        private static readonly HashSet<string> PackageFixtures = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Unity.Collections.Tests.CoreCLR.InternalJobNestedPrivate",
            "Unity.Collections.Tests.CoreCLR.PrivateJobNested",
            "Unity.Collections.Tests.CoreCLR.ProtectedJobNested",
            "Unity.Collections.Tests.CoreCLR.PublicJobPrivateGeneric"
        };

        public static bool IsForbiddenAssembly(string name) =>
            name.IndexOf("Tests", StringComparison.OrdinalIgnoreCase) >= 0
            || name.StartsWith("UnityEditor", StringComparison.OrdinalIgnoreCase)
            || name.Equals("Offtrack.Editor", StringComparison.OrdinalIgnoreCase)
            || name.Equals("Offtrack.Editor.dll", StringComparison.OrdinalIgnoreCase)
            || name.IndexOf("TestRunner", StringComparison.OrdinalIgnoreCase) >= 0
            || name.StartsWith("nunit", StringComparison.OrdinalIgnoreCase);

        public static string[] FilterForRelease(string[] assemblies)
        {
            var excluded = assemblies.Where(path => PackageFixtures.Contains(Path.GetFileNameWithoutExtension(path))).ToArray();
            var retained = assemblies.Except(excluded).ToArray();
            var forbidden = retained.Where(path => IsForbiddenAssembly(Path.GetFileName(path))).ToArray();
            if (forbidden.Length != 0)
                throw new BuildFailedException("Unexpected test/editor assemblies in release: " + string.Join(", ", forbidden));

            foreach (string path in retained)
            {
                using (var assembly = AssemblyDefinition.ReadAssembly(path))
                {
                    if (IsForbiddenAssembly(assembly.Name.Name))
                        throw new BuildFailedException("Test/editor assembly identity in release: " + assembly.Name.Name);
                    var dependencies = assembly.MainModule.AssemblyReferences
                        .Where(reference => PackageFixtures.Contains(reference.Name)).Select(reference => reference.Name).ToArray();
                    if (dependencies.Length != 0)
                        throw new BuildFailedException(Path.GetFileName(path) + " requires excluded package fixtures: "
                            + string.Join(", ", dependencies));
                }
            }
            return retained;
        }

        [Serializable] private sealed class ScriptAssemblyList { public string[] names = Array.Empty<string>(); }
        [Serializable] private sealed class InitializerList { public Initializer[] root = Array.Empty<Initializer>(); }
        [Serializable] private sealed class Initializer
        {
            public string assemblyName = "";
            public string className = "";
            public string methodName = "";
        }

        public static int ReconcileExcludedInitializers(string dataDirectory)
        {
            var scripts = JsonUtility.FromJson<ScriptAssemblyList>(
                File.ReadAllText(Path.Combine(dataDirectory, "ScriptingAssemblies.json")));
            if (scripts == null || scripts.names.Length == 0)
                throw new BuildFailedException("Missing Player assembly list before initializer reconciliation.");
            foreach (string fixture in PackageFixtures)
            {
                if (File.Exists(Path.Combine(dataDirectory, "Managed", fixture + ".dll"))
                    || scripts.names.Any(name => string.Equals(Path.GetFileNameWithoutExtension(name), fixture,
                        StringComparison.OrdinalIgnoreCase)))
                    throw new BuildFailedException("Cannot remove startup registrations while fixture is included: " + fixture);
            }

            string path = Path.Combine(dataDirectory, "RuntimeInitializeOnLoads.json");
            string json = File.ReadAllText(path);
            // Unity copies the pre-filter compilation list into this exported runtime file.
            // Retain raw JSON objects, not a partial DTO reserialization: future fields and
            // field/record order in every kept registration remain intact.
            var prefix = Regex.Match(json, "\\A\\s*\\{\\s*\"root\"\\s*:\\s*\\[");
            if (!prefix.Success) throw new BuildFailedException("Unexpected runtime initializer JSON schema.");
            var kept = new List<string>();
            int removed = 0;
            int cursor = prefix.Length;
            while (true)
            {
                while (cursor < json.Length && char.IsWhiteSpace(json[cursor])) cursor++;
                if (cursor >= json.Length) throw new BuildFailedException("Unterminated runtime initializer array.");
                if (json[cursor] == ']') break;
                int start = cursor;
                if (json[cursor] != '{') throw new BuildFailedException("Expected runtime initializer object.");
                int depth = 0;
                bool quoted = false, escaped = false;
                do
                {
                    char value = json[cursor++];
                    if (quoted)
                    {
                        if (escaped) escaped = false;
                        else if (value == '\\') escaped = true;
                        else if (value == '"') quoted = false;
                    }
                    else if (value == '"') quoted = true;
                    else if (value == '{') depth++;
                    else if (value == '}') depth--;
                } while (cursor < json.Length && (depth != 0 || quoted));
                if (depth != 0 || quoted) throw new BuildFailedException("Unterminated runtime initializer object.");
                string entry = json.Substring(start, cursor - start);
                var initializer = JsonUtility.FromJson<Initializer>(entry);
                if (initializer == null || string.IsNullOrEmpty(initializer.assemblyName))
                    throw new BuildFailedException("Runtime initializer has no assembly name.");
                if (PackageFixtures.Contains(initializer.assemblyName))
                {
                    if (initializer.methodName != "EarlyInit"
                        || !initializer.className.StartsWith("__JobReflectionRegistrationOutput__", StringComparison.Ordinal))
                        throw new BuildFailedException("Package fixture has an unaudited initializer: " + initializer.assemblyName);
                    removed++;
                }
                else kept.Add(entry);
                while (cursor < json.Length && char.IsWhiteSpace(json[cursor])) cursor++;
                if (cursor < json.Length && json[cursor] == ',') { cursor++; continue; }
                if (cursor < json.Length && json[cursor] == ']') break;
                throw new BuildFailedException("Unexpected runtime initializer separator.");
            }
            if (removed != 0)
                File.WriteAllText(path, json.Substring(0, prefix.Length) + string.Join(",", kept) + json.Substring(cursor),
                    new UTF8Encoding(false));
            return removed;
        }

        public static void ValidatePlayerMetadata(string dataDirectory)
        {
            string scriptPath = Path.Combine(dataDirectory, "ScriptingAssemblies.json");
            string initPath = Path.Combine(dataDirectory, "RuntimeInitializeOnLoads.json");
            var scripts = JsonUtility.FromJson<ScriptAssemblyList>(File.ReadAllText(scriptPath));
            var initializers = JsonUtility.FromJson<InitializerList>(File.ReadAllText(initPath));
            if (scripts == null || scripts.names.Length == 0 || initializers == null)
                throw new BuildFailedException("Missing or invalid Player assembly metadata.");
            var referencedNames = scripts.names.Concat(initializers.root.Select(item => item.assemblyName));
            var forbidden = referencedNames.Where(IsForbiddenAssembly).Distinct().ToArray();
            if (forbidden.Length != 0)
                throw new BuildFailedException("Test/editor entries remain in Player metadata: " + string.Join(", ", forbidden));
        }
    }

    public sealed class ReleaseAssemblyFilter : IFilterBuildAssemblies
    {
        public int callbackOrder => 0;

        public string[] OnFilterAssemblies(BuildOptions buildOptions, string[] assemblies)
        {
            if (!WindowsExport.IsBuildingPlayer) return assemblies;
            var retained = PlayerAssemblyPolicy.FilterForRelease(assemblies);
            foreach (var excluded in assemblies.Except(retained))
                Debug.Log("[OFFTRACK Export] Excluding Collections package fixture from compiled Player assembly list: "
                    + Path.GetFileName(excluded));
            return retained;
        }
    }
}
