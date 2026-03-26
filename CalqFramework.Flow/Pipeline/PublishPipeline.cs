namespace CalqFramework.Flow.Pipeline;

/// <summary>
///     Orchestrates the publish workflow: push packages to configured sources (§10).
/// </summary>
public static class PublishPipeline {
    /// <summary>
    ///     Pushes already-packed .nupkg files to all configured sources.
    /// </summary>
    public static List<string> Execute(List<string> nupkgPaths, List<string> sources, string sign, string apiKey, bool dryRun) {
        var publishedPackages = new List<string>();

        foreach (string nupkgPath in nupkgPaths) {
            string fileName = Path.GetFileNameWithoutExtension(nupkgPath);
            // NuGet filenames are {PackageId}.{Version} — version starts with a digit
            string[] parts = fileName.Split('.');
            int versionIndex = Array.FindIndex(parts, p => p.Length > 0 && char.IsDigit(p[0]));
            string packageId = versionIndex > 0
                ? string.Join('.', parts[..versionIndex])
                : fileName;

            if (dryRun) {
                Console.Error.WriteLine($"[dry-run] Would push {Path.GetFileName(nupkgPath)}");
                publishedPackages.Add(packageId);
                continue;
            }

            PushPackage(nupkgPath, sources, sign, apiKey);
            publishedPackages.Add(packageId);
        }

        return publishedPackages;
    }

    /// <summary>
    ///     Pushes a .nupkg to the specified sources (§3, §11, §14).
    /// </summary>
    private static void PushPackage(string nupkgPath, List<string> sources, string sign, string apiKey) {
        // §11: Optional signing
        if (!string.IsNullOrEmpty(sign)) {
            RUN($"dotnet nuget sign \"{nupkgPath}\" --certificate-fingerprint {sign}");
        }

        foreach (string source in sources) {
            string pushCmd = $"dotnet nuget push \"{nupkgPath}\" --source {source} --skip-duplicate";

            if (!string.IsNullOrEmpty(apiKey)) {
                pushCmd += $" --api-key {apiKey}";
            }

            RUN(pushCmd);
        }
    }
}
