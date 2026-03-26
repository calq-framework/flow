using System.Xml;
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

        if (testAssociations.TryGetValue(projectPath, out string? testProjectPath)) {
            RestoreProject(testProjectPath);
            RUN($"dotnet build \"{testProjectPath}\" -c Release {DeterministicFlags} {sourceLinkFlags}");
            RUN($"dotnet test \"{testProjectPath}\" -c Release --no-build");
        } else {
            RUN($"dotnet build \"{projectPath}\" -c Release {DeterministicFlags} {sourceLinkFlags}");
        }
    }

    /// <summary>
    ///     Resolves the base version DLL for comparison.
    ///     Strategy 1: Download from NuGet sources.
    ///     Strategy 2: Build from shadow copy (old source).
    ///     Returns the path to the base DLL, or null if unavailable.
    /// </summary>
    public static string? ResolveBaseDll(string projectPath, string projectName, Version baseVersion, List<string> sources, string? shadowCopyPath) {
        // Strategy 1: Try downloading from NuGet
        string? nugetDll = TryDownloadFromNuGet(projectName, baseVersion, $"{projectName}.dll");
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
        var packageRefs = doc.SelectNodes("/Project/ItemGroup/PackageReference");
        if (packageRefs != null) {
            foreach (XmlElement pkg in packageRefs) {
                string? include = pkg.GetAttribute("Include");
                if (include != null && include.StartsWith("Microsoft.SourceLink."))
                    return true;
            }
        }

        // Check for modern .NET 8+ implicit SourceLink via PublishRepositoryUrl
        var node = doc.SelectSingleNode("/Project/PropertyGroup/PublishRepositoryUrl");
        return node != null
            && bool.TryParse(node.InnerText.Trim(), out bool value)
            && value;
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
    ///     Uses a temporary project with dotnet restore to leverage NuGet's built-in source resolution.
    /// </summary>
    public static string? TryDownloadFromNuGet(string packageId, Version version, string searchPattern) {
        string versionStr = version.ToString(3);
        string tempPath = Path.Combine(Path.GetTempPath(), $"flow-nuget-{Guid.NewGuid():N}");

        try {
            Directory.CreateDirectory(tempPath);

            // Create a minimal project that references the target package
            string projectPath = Path.Combine(tempPath, "Probe.csproj");
            File.WriteAllText(projectPath, $"""
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net9.0</TargetFramework>
                  </PropertyGroup>
                  <ItemGroup>
                    <PackageReference Include="{packageId}" Version="{versionStr}" />
                  </ItemGroup>
                </Project>
                """);

            CMD($"dotnet restore \"{projectPath}\"");

            // The .nupkg is now in the global packages cache
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
            try { Directory.Delete(tempPath, true); } catch { }
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
