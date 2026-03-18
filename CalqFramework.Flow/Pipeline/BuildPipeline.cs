using static CalqFramework.Cmd.Terminal;

namespace CalqFramework.Flow.Pipeline;

/// <summary>
/// Executes the build, test, pack, and push pipeline (§11).
/// Execution order: restore → build (test project) → test → pack (library project) → push.
/// </summary>
public static class BuildPipeline {
    private const string DeterministicFlags =
        "-p:Deterministic=true -p:ContinuousIntegrationBuild=true " +
        "-p:PathMap=\"$(MSBuildProjectDirectory)=/src\"";

    /// <summary>
    /// Builds and packs a project inside the shadow copy.
    /// Returns the path to the generated .nupkg, or null on failure.
    /// </summary>
    public static string? BuildAndPack(
        string originalProjectPath,
        Dictionary<string, string> testAssociations,
        Version targetVersion,
        string shadowCopyPath) {
        var relativePath = Path.GetRelativePath(Environment.CurrentDirectory, originalProjectPath);
        var shadowProjectPath = Path.Combine(shadowCopyPath, relativePath);
        var versionStr = targetVersion.ToString(3);

        // §11: Restore with locked mode
        RUN($"dotnet restore \"{shadowProjectPath}\" --locked-mode");

        // §11: Build and test via test project if associated
        if (testAssociations.TryGetValue(originalProjectPath, out var testProjectPath)) {
            var relativeTestPath = Path.GetRelativePath(Environment.CurrentDirectory, testProjectPath);
            var shadowTestPath = Path.Combine(shadowCopyPath, relativeTestPath);

            RUN($"dotnet restore \"{shadowTestPath}\" --locked-mode");
            RUN($"dotnet build \"{shadowTestPath}\" -c Release {DeterministicFlags}");
            RUN($"dotnet test \"{shadowTestPath}\" -c Release --no-build");
        } else {
            RUN($"dotnet build \"{shadowProjectPath}\" -c Release {DeterministicFlags}");
        }

        // §11: Pack the library project at the target version
        RUN($"dotnet pack \"{shadowProjectPath}\" -c Release --no-build -p:PackageVersion={versionStr}");

        // Find the generated .nupkg
        var projectDir = Path.GetDirectoryName(shadowProjectPath)!;
        var nupkgs = Directory.GetFiles(projectDir, "*.nupkg", SearchOption.AllDirectories);
        return nupkgs.FirstOrDefault();
    }
}
