namespace CalqFramework.Flow.Diff;

/// <summary>
///     Compares assemblies at the IL/metadata level using MetadataLoadContext (§8).
/// </summary>
public static class SyntacticVersioning {
    /// <summary>
    ///     Compiler-generated attributes to ignore when comparing member identity strings.
    /// </summary>
    private static readonly HashSet<string> IgnoredAttributes = new(StringComparer.Ordinal) {
        "System.Runtime.CompilerServices.AsyncStateMachineAttribute",
        "System.Runtime.CompilerServices.NullableContextAttribute",
        "System.Runtime.CompilerServices.NullableAttribute",
        "System.Runtime.CompilerServices.CompilerGeneratedAttribute",
        "System.Runtime.CompilerServices.IteratorStateMachineAttribute"
    };

    /// <summary>
    ///     Compares the current assembly (already built) against the base version DLL.
    ///     The caller is responsible for providing the paths to both DLLs.
    /// </summary>
    public static ProjectDiffResult Compare(string projectName, string? currentDll, string? baseDll, bool ignoreAccessModifiers) {
        ProjectDiffResult result = new() {
            ProjectName = projectName
        };

        if (currentDll == null || baseDll == null) {
            result.ByteLevelFallback = true;
            if (currentDll != null) {
                // New project — no base to compare against
                result.Changes.Add(
                    new MemberChange {
                        MemberIdentity = projectName,
                        Kind = ChangeKind.Added
                    });
            }

            return result;
        }

        // Collect runtime assemblies from all shared frameworks (e.g. NETCore, ASP.NET Core).
        // The base runtime dir is e.g. .../shared/Microsoft.NETCore.App/9.0.x/
        // Sibling frameworks like Microsoft.AspNetCore.App live next to it.
        string runtimeDir = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
        string? sharedRoot = Path.GetDirectoryName(Path.GetDirectoryName(runtimeDir));
        IEnumerable<string> frameworkAssemblies = Directory.GetFiles(runtimeDir, "*.dll");
        if (sharedRoot != null && Directory.Exists(sharedRoot)) {
            frameworkAssemblies = Directory.GetDirectories(sharedRoot)
                .SelectMany(fw => {
                    // Pick the latest version directory within each framework
                    string? latest = Directory.GetDirectories(fw)
                        .OrderByDescending(d => Path.GetFileName(d))
                        .FirstOrDefault();
                    return latest != null ? Directory.GetFiles(latest, "*.dll") : [];
                });
        }
        string[] sharedAssemblies = frameworkAssemblies.ToArray();

        // Each MetadataLoadContext gets its own resolver with sibling DLLs from its
        // build directory only. Sharing a resolver causes FileLoadException when both
        // directories contain the same assembly identity (e.g. same-version dependency).
        IEnumerable<string> currentSiblings = Directory.GetFiles(Path.GetDirectoryName(currentDll)!, "*.dll");
        IEnumerable<string> baseSiblings = Directory.GetFiles(Path.GetDirectoryName(baseDll)!, "*.dll");

        // When the base DLL comes from the NuGet cache its directory may not contain
        // dependency assemblies (they live in separate package folders). Fall back to
        // the current build's siblings for type resolution — the dependency type names
        // are stable across versions so this is safe for signature extraction.
        HashSet<string> baseFileNames = new(baseSiblings.Select(Path.GetFileName)!, StringComparer.OrdinalIgnoreCase);
        IEnumerable<string> currentFallbacks = currentSiblings.Where(p => !baseFileNames.Contains(Path.GetFileName(p)));

        var currentResolver = new PathAssemblyResolver(sharedAssemblies.Concat(currentSiblings).Distinct());
        var baseResolver = new PathAssemblyResolver(sharedAssemblies.Concat(baseSiblings).Concat(currentFallbacks).Distinct());

        using var currentMlc = new MetadataLoadContext(currentResolver);
        using var baseMlc = new MetadataLoadContext(baseResolver);

        Assembly currentAssembly = currentMlc.LoadFromAssemblyPath(currentDll);
        Assembly baseAssembly = baseMlc.LoadFromAssemblyPath(baseDll);

        Dictionary<string, List<string>> currentMembers = ExtractMembers(currentAssembly, ignoreAccessModifiers);
        Dictionary<string, List<string>> baseMembers = ExtractMembers(baseAssembly, ignoreAccessModifiers);

        // Deleted members (breaking)
        foreach (string? member in baseMembers.Keys.Except(currentMembers.Keys)) {
            result.Changes.Add(
                new MemberChange {
                    MemberIdentity = member,
                    Kind = ChangeKind.Deleted
                });
        }

        // Added members (non-breaking)
        foreach (string? member in currentMembers.Keys.Except(baseMembers.Keys)) {
            result.Changes.Add(
                new MemberChange {
                    MemberIdentity = member,
                    Kind = ChangeKind.Added
                });
        }

        // Modified members — compare attributes
        foreach (string? member in currentMembers.Keys.Intersect(baseMembers.Keys)) {
            List<string> currentAttrs = currentMembers[member];
            List<string> baseAttrs = baseMembers[member];
            if (!currentAttrs.SequenceEqual(baseAttrs)) {
                result.Changes.Add(
                    new MemberChange {
                        MemberIdentity = member,
                        Kind = ChangeKind.AttributeModified
                    });
            }
        }

        // §8: Deterministic fallback — byte-level comparison if no syntactic changes found
        if (result.Changes.Count == 0 && !FilesAreEqual(currentDll, baseDll)) {
            result.ByteLevelFallback = true;
            result.Changes.Add(
                new MemberChange {
                    MemberIdentity = projectName,
                    Kind = ChangeKind.ILChanged
                });
        }

        return result;
    }

    private static Dictionary<string, List<string>> ExtractMembers(Assembly assembly, bool ignoreAccessModifiers) {
        var members = new Dictionary<string, List<string>>();

        foreach (Type type in assembly.GetTypes()) {
            if (!type.IsPublic && !ignoreAccessModifiers) {
                continue;
            }

            foreach (MemberInfo member in type.GetMembers(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)) {
                if (!ignoreAccessModifiers && !IsMemberAccessible(member)) {
                    continue;
                }

                string identity = $"{type.FullName}.{member.Name}({GetMemberSignature(member)})";
                List<string> attrs = GetFilteredAttributes(member);
                members[identity] = attrs;
            }
        }

        return members;
    }

    private static bool IsMemberAccessible(MemberInfo member) =>
        member switch {
            MethodInfo m => m.IsPublic || m.IsFamily || m.IsFamilyOrAssembly,
            FieldInfo f => f.IsPublic || f.IsFamily || f.IsFamilyOrAssembly,
            PropertyInfo p => (p.GetMethod?.IsPublic ?? false) || (p.SetMethod?.IsPublic ?? false),
            EventInfo e => e.AddMethod?.IsPublic ?? false,
            TypeInfo t => t.IsPublic || t.IsNestedPublic,
            _ => false
        };

    private static string GetMemberSignature(MemberInfo member) =>
        member switch {
            MethodInfo m => string.Join(
                ",",
                m.GetParameters()
                    .Select(p => p.ParameterType.FullName)),
            ConstructorInfo c => string.Join(
                ",",
                c.GetParameters()
                    .Select(p => p.ParameterType.FullName)),
            PropertyInfo p => p.PropertyType.FullName ?? "",
            FieldInfo f => f.FieldType.FullName ?? "",
            _ => ""
        };

    private static List<string> GetFilteredAttributes(MemberInfo member) =>
        [.. member.GetCustomAttributesData()
            .Where(a => !IgnoredAttributes.Contains(a.AttributeType.FullName ?? ""))
            .Select(a => a.ToString())
            .OrderBy(a => a)];

    /// <summary>
    ///     Searches for a compiled assembly DLL in standard output directories.
    /// </summary>
    public static string? FindAssembly(string baseDir, string projectName) {
        string[] searchPaths = [
            Path.Combine(baseDir, "bin", "Release"),
            Path.Combine(baseDir, "bin", "Debug"),
            baseDir
        ];

        foreach (string? searchPath in searchPaths) {
            if (!Directory.Exists(searchPath)) {
                continue;
            }

            string[] dlls = Directory.GetFiles(searchPath, $"{projectName}.dll", SearchOption.AllDirectories);
            if (dlls.Length > 0) {
                return dlls[0];
            }
        }

        return null;
    }

    private static bool FilesAreEqual(string path1, string path2) {
        byte[] bytes1 = File.ReadAllBytes(path1);
        byte[] bytes2 = File.ReadAllBytes(path2);
        return bytes1.AsSpan()
            .SequenceEqual(bytes2);
    }
}
