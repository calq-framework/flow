using CalqFramework.Flow.Diff;
using static CalqFramework.Cmd.Terminal;

namespace CalqFramework.Flow.Pipeline;

/// <summary>
/// Builds projects and resolves base DLLs for comparison (§11, §13).
/// </summary>
public static class BuildPipeline {
    private const string DeterministicFlags =
        "-p:Deterministic=true -p:ContinuousIntegrationBuild=true " +
        "-p:PathMap=\"$(MSBuildProjectDirectory)=/src\"";

    /// <summary>
    /// Builds the current project in the working directory.
    /// Also builds and runs the associated test project if one exists.
    /// </summary>
    public static void BuildCurrent(
        string projectPath,
        Dictionary<string, string> testAssociations) {
        RUN($"dotnet restore \"{projectPath}\" --locked-mode");

        if (testAssociations.TryGetValue(projectPath, out var testProjectPath)) {
            RUN($"dotnet restore \"{testProjectPath}\" --locked-mode");
            RUN($"dotnet build \"{testProjectPath}\" -c Release {DeterministicFlags}");
            RUN($"dotnet test \"{testProjectPath}\" -c Release --no-build");
        } else {
            RUN($"dotnet build \"{projectPath}\" -c Release {DeterministicFlags}");
        }
    }

    /// <summary>
    /// Resolves the base version DLL for comparison.
    /// Strategy 1: Download from NuGet sources.
    /// Strategy 2: Build from shadow copy (old source).
    /// Returns the path to the base DLL, or null if unavailable.
    /// </summary>
    public static string? ResolveBaseDll(
        string projectPath, string projectName,
        Version baseVersion, List<string> sources,
        string? shadowCopyPath) {
        // Strategy 1: Try downloading from NuGet
        var nugetDll = TryDownloadFromNuGet(projectName, baseVersion, sources);
        if (nugetDll != null) return nugetDll;

        // Strategy 2: Build from shadow copy
        if (shadowCopyPath != null) {
            return BuildInShadowCopy(projectPath, projectName, shadowCopyPath);
        }

        return null;
    }

    /// <summary>
    /// Packs the current project at the target version. Reuses the existing build.
    /// Returns the path to the generated .nupkg, or null on failure.
    /// </summary>
    public static string? Pack(string projectPath, Version targetVersion) {
        var versionStr = targetVersion.ToString(3);
        RUN($"dotnet pack \"{projectPath}\" -c Release --no-build -p:PackageVersion={versionStr}");

        var projectDir = Path.GetDirectoryName(projectPath)!;
        var nupkgs = Directory.GetFiles(projectDir, "*.nupkg", SearchOption.AllDirectories);
        return nupkgs.FirstOrDefault();
    }

    /// <summary>
    /// Attempts to download a package from configured NuGet sources and extract its DLL.
    /// </summary>
    private static string? TryDownloadFromNuGet(
        string packageId, Version version, List<string> sources) {
        var versionStr = version.ToString(3);
        var tempPath = Path.Combine(Path.GetTempPath(), $"flow-nuget-{Guid.NewGuid():N}");

        try {
            Directory.CreateDirectory(tempPath);

            foreach (var source in sources) {
                try {
                    CMD($"dotnet nuget download {packageId} --version {versionStr} --source {source} --output-directory \"{tempPath}\"");
                } catch {
                    // nuget download is .NET 9+ — fall back to nuget install
                    try {
                        CMD($"nuget install {packageId} -Version {versionStr} -Source {source} -OutputDirectory \"{tempPath}\" -NonInteractive");
                    } catch {
                        continue;
                    }
                }

                // Find the DLL inside the downloaded package
                var dlls = Directory.GetFiles(tempPath, $"{packageId}.dll", SearchOption.AllDirectories);
                if (dlls.Length > 0) return dlls[0];
            }

            return null;
        } catch {
            return null;
        }
        // Note: temp directory is intentionally NOT cleaned up here —
        // the DLL path is returned and used by the caller. Cleanup happens
        // at the end of the publish pipeline via CleanupTempDirs.
    }

    /// <summary>
    /// Builds the old version of a project inside the shadow copy.
    /// </summary>
    private static string? BuildInShadowCopy(
        string projectPath, string projectName, string shadowCopyPath) {
        var workingDir = PWD;
        var relativePath = Path.GetRelativePath(workingDir, projectPath);
        var shadowProjectPath = Path.Combine(shadowCopyPath, relativePath);

        if (!File.Exists(shadowProjectPath)) return null;

        try {
            RUN($"dotnet restore \"{shadowProjectPath}\"");
            RUN($"dotnet build \"{shadowProjectPath}\" -c Release {DeterministicFlags}");
        } catch {
            return null;
        }

        return SyntacticVersioning.FindAssembly(
            Path.GetDirectoryName(shadowProjectPath)!, projectName);
    }
}
