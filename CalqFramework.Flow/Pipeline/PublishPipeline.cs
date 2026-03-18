using CalqFramework.Flow.Git;
using static CalqFramework.Cmd.Terminal;

namespace CalqFramework.Flow.Pipeline;

/// <summary>
/// Orchestrates the publish workflow: mirroring → source fallback → push (§10).
/// </summary>
public static class PublishPipeline {
    /// <summary>
    /// Executes the full publish pipeline for changed projects.
    /// </summary>
    public static List<string> Execute(
        List<string> allProjects,
        List<string> changedProjects,
        Dictionary<string, string> testAssociations,
        Version targetVersion,
        List<string> sources,
        string sign,
        bool dryRun,
        string remote,
        string tagPrefix) {
        var publishedPackages = new List<string>();

        foreach (var project in changedProjects) {
            var packageId = Path.GetFileNameWithoutExtension(project);
            var versionStr = targetVersion.ToString(3);

            if (dryRun) {
                Console.Error.WriteLine($"[dry-run] Would publish {packageId} {versionStr}");
                publishedPackages.Add(packageId);
                continue;
            }

            // §10 Phase 2: Strategy 1 — Mirroring
            var mirrored = TryMirror(packageId, versionStr, sources, sign);
            if (mirrored) {
                publishedPackages.Add(packageId);
                continue;
            }

            // §10 Phase 3: Strategy 2 — Source Fallback (Shadow Copy Build)
            var workingDirectory = Environment.CurrentDirectory;
            var repoRoot = GitOperations.GetRepositoryRoot(workingDirectory);
            var shadowCopyPath = ShadowCopy.Create(workingDirectory, repoRoot, remote, tagPrefix);

            try {
                var nupkgPath = BuildPipeline.BuildAndPack(
                    project, testAssociations, targetVersion, shadowCopyPath);

                if (nupkgPath != null) {
                    PushPackage(nupkgPath, sources, sign);
                    publishedPackages.Add(packageId);
                }
            } finally {
                ShadowCopy.Cleanup(shadowCopyPath);
            }
        }

        return publishedPackages;
    }

    /// <summary>
    /// Attempts to mirror a package from an existing NuGet source (§10 Phase 2).
    /// </summary>
    private static bool TryMirror(
        string packageId, string version, List<string> sources, string sign) {
        var tempPath = Path.Combine(Path.GetTempPath(), $"flow-mirror-{Guid.NewGuid():N}");

        try {
            Directory.CreateDirectory(tempPath);
            CMD($"nuget install {packageId} -Version {version} -OutputDirectory \"{tempPath}\"");

            // Find the specific package directory
            var packageDir = Directory.GetDirectories(tempPath, $"{packageId}.{version}")
                .FirstOrDefault();
            if (packageDir == null) return false;

            // Find the .nupkg
            var nupkg = Directory.GetFiles(packageDir, "*.nupkg", SearchOption.AllDirectories)
                .FirstOrDefault();
            if (nupkg == null) return false;

            // Push the mirrored package and any dependency packages
            var allNupkgs = Directory.GetFiles(tempPath, "*.nupkg", SearchOption.AllDirectories);
            foreach (var pkg in allNupkgs) {
                PushPackage(pkg, sources, sign);
            }

            return true;
        } catch {
            return false;
        } finally {
            if (Directory.Exists(tempPath)) {
                Directory.Delete(tempPath, true);
            }
        }
    }

    /// <summary>
    /// Pushes a .nupkg to all configured sources (§3, §11, §14).
    /// </summary>
    private static void PushPackage(string nupkgPath, List<string> sources, string sign) {
        // §11: Optional signing
        if (!string.IsNullOrEmpty(sign)) {
            RUN($"dotnet nuget sign \"{nupkgPath}\" --certificate-fingerprint {sign}");
        }

        foreach (var source in sources) {
            var pushCmd = $"dotnet nuget push \"{nupkgPath}\" --source {source} --skip-duplicate";

            // §3: Special case for nuget.org — use NUGET_API_KEY
            if (source.Equals("nuget.org", StringComparison.OrdinalIgnoreCase)) {
                var apiKey = Environment.GetEnvironmentVariable("NUGET_API_KEY") ?? "";
                pushCmd += $" --api-key {apiKey}";
            }

            RUN(pushCmd);
        }
    }
}
