using CalqFramework.Flow.Diff;

namespace CalqFramework.Flow.Pipeline;

/// <summary>
///     Builds projects and resolves base DLLs for comparison (§11, §13).
/// </summary>
public static class BuildPipeline {
    private const string DeterministicFlags = "-p:Deterministic=true -p:ContinuousIntegrationBuild=true " + "-p:PathMap=\"$(MSBuildProjectDirectory)=/src\"";
    private const string SourceLinkBuildFlags = "-p:EmbedUntrackedSources=true -p:DebugType=embedded";
    private const string SourceLinkPackFlags = "-p:PublishRepositoryUrl=true";

    /// <summary>
    ///     Builds the current project in the working directory.
    ///     Also builds and runs the associated test project if one exists.
    /// </summary>
    public static void BuildCurrent(string projectPath, Dictionary<string, string> testAssociations) {
        RestoreProject(projectPath);
        string sourceLinkFlags = HasSourceLink(projectPath) ? SourceLinkBuildFlags : "";

        // Build the project with --no-incremental to ensure deterministic/source-link
        // flags are applied even if it was previously compiled as a transitive dependency
        // of another project without those flags. Use --no-dependencies to avoid rebuilding
        // already-built ProjectReference dependencies (which may have different flags).
        RUN($"dotnet build \"{projectPath}\" -c Release --no-incremental --no-dependencies {DeterministicFlags} {sourceLinkFlags}");

        if (testAssociations.TryGetValue(projectPath, out string? testProjectPath)) {
            RestoreProject(testProjectPath);
            RUN($"dotnet build \"{testProjectPath}\" -c Release --no-dependencies {DeterministicFlags} {sourceLinkFlags}");
            RUN($"dotnet test \"{testProjectPath}\" -c Release --no-build");
        }
    }

    /// <summary>
    ///     Builds projects in dependency order using --no-dependencies to ensure each
    ///     project is compiled with its own correct flags. Uses a best-effort topological
    ///     sort based on ProjectReference, with a queue-retry fallback for edge cases
    ///     (conditional references, imported props, etc.).
    ///     When <paramref name="projectsToBuild"/> is a subset of <paramref name="allProjects"/>,
    ///     their transitive dependencies are included automatically.
    ///     Returns the set of normalized paths that were successfully built.
    /// </summary>
    public static HashSet<string> BuildAll(List<string> projectsToBuild, List<string> allProjects, Dictionary<string, string> testAssociations) {
        var buildSet = GetTransitiveClosure(projectsToBuild, allProjects);
        List<string> sorted = TopologicalSort(allProjects)
            .Where(p => buildSet.Contains(Path.GetFullPath(p)))
            .ToList();

        var built = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var queue = new Queue<string>(sorted);
        int failedSinceLastSuccess = 0;

        while (queue.Count > 0) {
            string project = queue.Dequeue();
            try {
                BuildCurrent(project, testAssociations);
                built.Add(Path.GetFullPath(project));
                failedSinceLastSuccess = 0;
            } catch {
                failedSinceLastSuccess++;
                if (failedSinceLastSuccess >= queue.Count + 1) {
                    // No progress — a full pass with no successes. Re-throw the error.
                    throw;
                }

                queue.Enqueue(project);
            }
        }

        return built;
    }

    /// <summary>
    ///     Expands a set of projects to include their transitive ProjectReference dependencies
    ///     within the full project list.
    /// </summary>
    private static HashSet<string> GetTransitiveClosure(List<string> seeds, List<string> allProjects) {
        var pathToProject = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string p in allProjects) {
            pathToProject[Path.GetFullPath(p)] = p;
        }

        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var stack = new Stack<string>(seeds.Select(Path.GetFullPath));

        while (stack.Count > 0) {
            string current = stack.Pop();
            if (!result.Add(current)) continue;

            if (pathToProject.TryGetValue(current, out string? projectPath)) {
                foreach (string dep in GetProjectReferences(projectPath)) {
                    string normalizedDep = Path.GetFullPath(dep);
                    if (!result.Contains(normalizedDep)) {
                        stack.Push(normalizedDep);
                    }
                }
            }
        }

        return result;
    }

    /// <summary>
    ///     Best-effort topological sort of projects based on ProjectReference elements.
    ///     Leaves (projects with no local dependencies) come first.
    ///     Falls back gracefully if references can't be resolved.
    /// </summary>
    private static List<string> TopologicalSort(List<string> projects) {
        // Map normalized project paths to their index for quick lookup
        var pathToIndex = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < projects.Count; i++) {
            pathToIndex[Path.GetFullPath(projects[i])] = i;
        }

        // Build adjacency: dependencies[i] = set of indices that project i depends on
        var dependencies = new List<HashSet<int>>();
        for (int i = 0; i < projects.Count; i++) {
            dependencies.Add([]);
        }

        for (int i = 0; i < projects.Count; i++) {
            foreach (string refPath in GetProjectReferences(projects[i])) {
                string normalizedRef = Path.GetFullPath(refPath);
                if (pathToIndex.TryGetValue(normalizedRef, out int depIndex)) {
                    dependencies[i].Add(depIndex);
                }
            }
        }

        // Kahn's algorithm: repeatedly pick projects with no unresolved dependencies
        var result = new List<string>();
        var available = new Queue<int>();
        var inDegree = new int[projects.Count];

        // Compute in-degree (how many projects depend on each)
        // Actually we need: for each project, how many of its dependencies are not yet built
        var remaining = dependencies.Select(d => new HashSet<int>(d)).ToList();

        for (int i = 0; i < projects.Count; i++) {
            if (remaining[i].Count == 0) {
                available.Enqueue(i);
            }
        }

        while (available.Count > 0) {
            int idx = available.Dequeue();
            result.Add(projects[idx]);

            // Remove this project from others' dependency sets
            for (int i = 0; i < projects.Count; i++) {
                if (remaining[i].Remove(idx) && remaining[i].Count == 0) {
                    available.Enqueue(i);
                }
            }
        }

        // Append any projects not reached (shouldn't happen in valid repos, but be safe)
        for (int i = 0; i < projects.Count; i++) {
            if (!result.Contains(projects[i])) {
                result.Add(projects[i]);
            }
        }

        return result;
    }

    /// <summary>
    ///     Parses ProjectReference Include paths from a csproj file.
    ///     Returns absolute paths resolved relative to the project's directory.
    /// </summary>
    private static List<string> GetProjectReferences(string projectPath) {
        var refs = new List<string>();
        string projectDir = Path.GetDirectoryName(Path.GetFullPath(projectPath))!;

        try {
            var doc = new XmlDocument();
            doc.Load(projectPath);

            XmlNodeList? nodes = doc.SelectNodes("/Project/ItemGroup/ProjectReference");
            if (nodes != null) {
                foreach (XmlElement node in nodes) {
                    string? include = node.GetAttribute("Include");
                    if (!string.IsNullOrEmpty(include)) {
                        string fullPath = Path.GetFullPath(Path.Combine(projectDir, include));
                        refs.Add(fullPath);
                    }
                }
            }
        } catch {
            // If parsing fails, return empty — the queue retry will handle ordering
        }

        return refs;
    }

    /// <summary>
    ///     Resolves the base version DLL for comparison.
    ///     Strategy 1: Download from NuGet sources.
    ///     Strategy 2: Build from shadow copy (old source).
    ///     Returns the path to the base DLL, or null if unavailable.
    /// </summary>
    public static string? ResolveBaseDll(string projectPath, string projectName, Version baseVersion, List<string> sources, string? shadowCopyPath) {
        // Strategy 1: Try downloading from NuGet
        string? nugetDll = TryDownloadFromNuGet(projectPath, projectName, baseVersion, $"{projectName}.dll");
        if (nugetDll != null) {
            return nugetDll;
        }

        // Strategy 2: Build from shadow copy
        if (shadowCopyPath != null) {
            return BuildInShadowCopy(projectPath, projectName, shadowCopyPath);
        }

        return null;
    }

    /// <summary>
    ///     Packs the current project at the target version. Reuses the existing build.
    ///     Returns the path to the generated .nupkg, or null on failure.
    /// </summary>
    public static string? Pack(string projectPath, Version targetVersion) {
        string versionStr = targetVersion.ToString(3);
        string sourceLinkFlags = HasSourceLink(projectPath) ? $"{SourceLinkBuildFlags} {SourceLinkPackFlags}" : "";
        RUN($"dotnet pack \"{projectPath}\" -c Release --no-build -p:PackageVersion={versionStr} {sourceLinkFlags}");

        string projectDir = Path.GetDirectoryName(projectPath)!;
        string[] nupkgs = Directory.GetFiles(projectDir, "*.nupkg", SearchOption.AllDirectories);
        return nupkgs.FirstOrDefault();
    }

    /// <summary>
    ///     Detects whether a project uses SourceLink, either via an explicit package
    ///     reference (Microsoft.SourceLink.*) or the modern .NET 8+ implicit support
    ///     indicated by PublishRepositoryUrl being set to true.
    /// </summary>
    private static bool HasSourceLink(string projectPath) {
        var doc = new XmlDocument();
        doc.Load(projectPath);

        // Check for explicit SourceLink package reference
        XmlNodeList? packageRefs = doc.SelectNodes("/Project/ItemGroup/PackageReference");
        if (packageRefs != null) {
            foreach (XmlElement pkg in packageRefs) {
                string? include = pkg.GetAttribute("Include");
                if (include != null && include.StartsWith("Microsoft.SourceLink.")) {
                    return true;
                }
            }
        }

        // Check for modern .NET 8+ implicit SourceLink via PublishRepositoryUrl
        XmlNode? node = doc.SelectSingleNode("/Project/PropertyGroup/PublishRepositoryUrl");
        return node != null && bool.TryParse(node.InnerText.Trim(), out bool value) && value;
    }
    /// <summary>
    ///     Detects whether a project is a .NET tool package (PackAsTool).
    /// </summary>
    private static bool IsToolPackage(string projectPath) {
        var doc = new XmlDocument();
        doc.Load(projectPath);
        XmlNode? node = doc.SelectSingleNode("/Project/PropertyGroup/PackAsTool");
        return node != null && bool.TryParse(node.InnerText.Trim(), out bool value) && value;
    }


    /// <summary>
    ///     Restores a project, using --locked-mode if a packages.lock.json exists.
    ///     Emits a warning when no lock file is found.
    /// </summary>
    private static void RestoreProject(string projectPath) {
        string projectDir = Path.GetDirectoryName(projectPath)!;
        string lockFilePath = Path.Combine(projectDir, "packages.lock.json");

        if (File.Exists(lockFilePath)) {
            RUN($"dotnet restore \"{projectPath}\" --locked-mode");
        } else {
            Console.Error.WriteLine($"[Warning] No packages.lock.json found for {Path.GetFileNameWithoutExtension(projectPath)}. Consider enabling RestorePackagesWithLockFile for reproducible builds.");
            RUN($"dotnet restore \"{projectPath}\"");
        }
    }

    /// <summary>
    ///     Attempts to download a package from all configured NuGet sources and find a matching file.
    ///     Uses dotnet restore for library packages and dotnet tool install for tool packages.
    /// </summary>
    public static string? TryDownloadFromNuGet(string projectPath, string packageId, Version version, string searchPattern) {
        string versionStr = version.ToString(3);
        string tempPath = Path.Combine(Path.GetTempPath(), $"flow-nuget-{Guid.NewGuid():N}");
        bool deleteTempPath = true;

        try {
            Directory.CreateDirectory(tempPath);

            if (IsToolPackage(projectPath)) {
                // Tool packages cannot be referenced via PackageReference (NU1212).
                // dotnet tool install places the package in {tool-path}/.store/.
                string toolPath = Path.Combine(tempPath, "tools");
                RUN($"dotnet tool install {packageId} --version {versionStr} --tool-path \"{toolPath}\"");

                // Search the .store for the requested file pattern.
                string storePath = Path.Combine(toolPath, ".store", packageId.ToLowerInvariant(), versionStr);
                if (Directory.Exists(storePath)) {
                    string[] files = Directory.GetFiles(storePath, searchPattern, SearchOption.AllDirectories);
                    if (files.Length > 0) {
                        deleteTempPath = false; // keep alive — caller holds a reference
                        return files[0];
                    }
                }

                return null;
            } else {
                // Library packages: create a temporary project and restore.
                // Derive the TFM from the running runtime so the probe stays compatible
                // with packages that target newer frameworks (e.g. net10.0).
                string tfm = $"net{Environment.Version.Major}.0";
                string probeProjectPath = Path.Combine(tempPath, "Probe.csproj");
                File.WriteAllText(
                    probeProjectPath,
                    $"""
                     <Project Sdk="Microsoft.NET.Sdk">
                       <PropertyGroup>
                         <TargetFramework>{tfm}</TargetFramework>
                       </PropertyGroup>
                       <ItemGroup>
                         <PackageReference Include="{packageId}" Version="{versionStr}" />
                       </ItemGroup>
                     </Project>
                     """);

                RUN($"dotnet restore \"{probeProjectPath}\"");
            }

            // The package is now in the global packages cache
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string cachePath = Path.Combine(home, ".nuget", "packages", packageId.ToLowerInvariant(), versionStr);

            if (Directory.Exists(cachePath)) {
                string[] files = Directory.GetFiles(cachePath, searchPattern, SearchOption.AllDirectories);
                if (files.Length > 0) {
                    return files[0];
                }
            }

            // Also check for the .nupkg itself in the cache
            string nupkgInCache = Path.Combine(cachePath, $"{packageId.ToLowerInvariant()}.{versionStr}.nupkg");
            if (searchPattern == "*.nupkg" && File.Exists(nupkgInCache)) {
                return nupkgInCache;
            }

            return null;
        } catch {
            return null;
        } finally {
            if (deleteTempPath) {
                try {
                    Directory.Delete(tempPath, true);
                } catch {
                }
            }
        }
    }

    /// <summary>
    ///     Builds the old version of a project inside the shadow copy.
    /// </summary>
    private static string? BuildInShadowCopy(string projectPath, string projectName, string shadowCopyPath) {
        string workingDir = PWD;
        string relativePath = Path.GetRelativePath(workingDir, projectPath);
        string shadowProjectPath = Path.Combine(shadowCopyPath, relativePath);

        if (!File.Exists(shadowProjectPath)) {
            return null;
        }

        try {
            RUN($"dotnet restore \"{shadowProjectPath}\"");
            RUN($"dotnet build \"{shadowProjectPath}\" -c Release {DeterministicFlags}");
        } catch {
            return null;
        }

        return SyntacticVersioning.FindAssembly(Path.GetDirectoryName(shadowProjectPath)!, projectName);
    }
}
