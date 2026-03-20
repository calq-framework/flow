namespace CalqFramework.Flow.Pipeline;

/// <summary>
///     Orchestrates the publish workflow: push packages to configured sources (§10).
/// </summary>
public static class PublishPipeline {
    /// <summary>
    ///     Pushes already-packed .nupkg files to all configured sources.
    /// </summary>
    public static List<string> Execute(List<string> nupkgPaths, List<string> sources, string sign, bool dryRun) {
        var publishedPackages = new List<string>();

        foreach (string nupkgPath in nupkgPaths) {
            string packageId = Path.GetFileNameWithoutExtension(nupkgPath)
                .Split('.')[0]; // rough extraction from "Name.1.2.3.nupkg"

            if (dryRun) {
                Console.Error.WriteLine($"[dry-run] Would push {Path.GetFileName(nupkgPath)}");
                publishedPackages.Add(packageId);
                continue;
            }

            PushPackage(nupkgPath, sources, sign);
            publishedPackages.Add(packageId);
        }

        return publishedPackages;
    }

    /// <summary>
    ///     Pushes a .nupkg to all configured sources (§3, §11, §14).
    /// </summary>
    private static void PushPackage(string nupkgPath, List<string> sources, string sign) {
        // §11: Optional signing
        if (!string.IsNullOrEmpty(sign)) {
            RUN($"dotnet nuget sign \"{nupkgPath}\" --certificate-fingerprint {sign}");
        }

        foreach (string source in sources) {
            string pushCmd = $"dotnet nuget push \"{nupkgPath}\" --source {source} --skip-duplicate";

            // §3: Special case for nuget.org — use NUGET_API_KEY
            if (source.Equals("nuget.org", StringComparison.OrdinalIgnoreCase)) {
                string apiKey = Environment.GetEnvironmentVariable("NUGET_API_KEY") ?? "";
                pushCmd += $" --api-key {apiKey}";
            }

            RUN(pushCmd);
        }
    }
}
