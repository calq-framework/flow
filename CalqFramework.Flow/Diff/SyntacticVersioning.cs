using System.Reflection;
using static CalqFramework.Cmd.Terminal;

namespace CalqFramework.Flow.Diff;

/// <summary>
/// Compares assemblies at the IL/metadata level using MetadataLoadContext (§8).
/// </summary>
public static class SyntacticVersioning {
    /// <summary>
    /// Compiler-generated attributes to ignore when comparing member identity strings.
    /// </summary>
    private static readonly HashSet<string> IgnoredAttributes = new(StringComparer.Ordinal) {
        "System.Runtime.CompilerServices.AsyncStateMachineAttribute",
        "System.Runtime.CompilerServices.NullableContextAttribute",
        "System.Runtime.CompilerServices.NullableAttribute",
        "System.Runtime.CompilerServices.CompilerGeneratedAttribute",
        "System.Runtime.CompilerServices.IteratorStateMachineAttribute"
    };

    /// <summary>
    /// Compares the current assembly (already built) against the base version DLL.
    /// The caller is responsible for providing the paths to both DLLs.
    /// </summary>
    public static ProjectDiffResult Compare(
        string projectName, string? currentDll, string? baseDll,
        bool ignoreAccessModifiers) {
        var result = new ProjectDiffResult { ProjectName = projectName };

        if (currentDll == null || baseDll == null) {
            result.ByteLevelFallback = true;
            if (currentDll != null) {
                // New project — no base to compare against
                result.Changes.Add(new MemberChange {
                    MemberIdentity = projectName,
                    Kind = ChangeKind.Added
                });
            }
            return result;
        }

        var runtimeDir = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
        var runtimeAssemblies = Directory.GetFiles(runtimeDir, "*.dll");

        var resolver = new PathAssemblyResolver(runtimeAssemblies
            .Append(currentDll).Append(baseDll));

        using var currentMlc = new MetadataLoadContext(resolver);
        using var baseMlc = new MetadataLoadContext(resolver);

        var currentAssembly = currentMlc.LoadFromAssemblyPath(currentDll);
        var baseAssembly = baseMlc.LoadFromAssemblyPath(baseDll);

        var currentMembers = ExtractMembers(currentAssembly, ignoreAccessModifiers);
        var baseMembers = ExtractMembers(baseAssembly, ignoreAccessModifiers);

        // Deleted members (breaking)
        foreach (var member in baseMembers.Keys.Except(currentMembers.Keys)) {
            result.Changes.Add(new MemberChange {
                MemberIdentity = member,
                Kind = ChangeKind.Deleted
            });
        }

        // Added members (non-breaking)
        foreach (var member in currentMembers.Keys.Except(baseMembers.Keys)) {
            result.Changes.Add(new MemberChange {
                MemberIdentity = member,
                Kind = ChangeKind.Added
            });
        }

        // Modified members — compare attributes
        foreach (var member in currentMembers.Keys.Intersect(baseMembers.Keys)) {
            var currentAttrs = currentMembers[member];
            var baseAttrs = baseMembers[member];
            if (!currentAttrs.SequenceEqual(baseAttrs)) {
                result.Changes.Add(new MemberChange {
                    MemberIdentity = member,
                    Kind = ChangeKind.AttributeModified
                });
            }
        }

        // §8: Deterministic fallback — byte-level comparison if no syntactic changes found
        if (result.Changes.Count == 0 && !FilesAreEqual(currentDll, baseDll)) {
            result.ByteLevelFallback = true;
            result.Changes.Add(new MemberChange {
                MemberIdentity = projectName,
                Kind = ChangeKind.ILChanged
            });
        }

        return result;
    }

    private static Dictionary<string, List<string>> ExtractMembers(
        Assembly assembly, bool ignoreAccessModifiers) {
        var members = new Dictionary<string, List<string>>();

        foreach (var type in assembly.GetTypes()) {
            if (!type.IsPublic && !ignoreAccessModifiers) continue;

            foreach (var member in type.GetMembers(
                BindingFlags.Public | BindingFlags.NonPublic |
                BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)) {
                if (!ignoreAccessModifiers && !IsMemberAccessible(member)) continue;

                var identity = $"{type.FullName}.{member.Name}({GetMemberSignature(member)})";
                var attrs = GetFilteredAttributes(member);
                members[identity] = attrs;
            }
        }

        return members;
    }

    private static bool IsMemberAccessible(MemberInfo member) {
        return member switch {
            MethodInfo m => m.IsPublic || m.IsFamily || m.IsFamilyOrAssembly,
            FieldInfo f => f.IsPublic || f.IsFamily || f.IsFamilyOrAssembly,
            PropertyInfo p => (p.GetMethod?.IsPublic ?? false) || (p.SetMethod?.IsPublic ?? false),
            EventInfo e => e.AddMethod?.IsPublic ?? false,
            TypeInfo t => t.IsPublic || t.IsNestedPublic,
            _ => false
        };
    }

    private static string GetMemberSignature(MemberInfo member) {
        return member switch {
            MethodInfo m => string.Join(",", m.GetParameters().Select(p => p.ParameterType.FullName)),
            ConstructorInfo c => string.Join(",", c.GetParameters().Select(p => p.ParameterType.FullName)),
            PropertyInfo p => p.PropertyType.FullName ?? "",
            FieldInfo f => f.FieldType.FullName ?? "",
            _ => ""
        };
    }

    private static List<string> GetFilteredAttributes(MemberInfo member) {
        return member.GetCustomAttributesData()
            .Where(a => !IgnoredAttributes.Contains(a.AttributeType.FullName ?? ""))
            .Select(a => a.ToString())
            .OrderBy(a => a)
            .ToList();
    }

    /// <summary>
    /// Searches for a compiled assembly DLL in standard output directories.
    /// </summary>
    public static string? FindAssembly(string baseDir, string projectName) {
        var searchPaths = new[] {
            Path.Combine(baseDir, "bin", "Release"),
            Path.Combine(baseDir, "bin", "Debug"),
            baseDir
        };

        foreach (var searchPath in searchPaths) {
            if (!Directory.Exists(searchPath)) continue;
            var dlls = Directory.GetFiles(searchPath, $"{projectName}.dll", SearchOption.AllDirectories);
            if (dlls.Length > 0) return dlls[0];
        }

        return null;
    }

    private static bool FilesAreEqual(string path1, string path2) {
        var bytes1 = File.ReadAllBytes(path1);
        var bytes2 = File.ReadAllBytes(path2);
        return bytes1.AsSpan().SequenceEqual(bytes2);
    }
}
