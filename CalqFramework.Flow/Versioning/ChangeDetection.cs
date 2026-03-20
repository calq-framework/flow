namespace CalqFramework.Flow.Versioning;

/// <summary>
///     Detects which projects have changed since the last version tag (§7).
///     A project is "changed" only if a file modification occurs within
///     the project's own directory or its subdirectories.
/// </summary>
public static class ChangeDetection {
    /// <summary>
    ///     Returns the subset of projects that have file changes since the last tag.
    /// </summary>
    public static List<string> DetectChangedProjects(List<string> projects, string remote, string tagPrefix) {
        List<string> changedFiles = GetChangedFilesSinceLastTag(remote, tagPrefix);
        if (changedFiles.Count == 0) {
            return [];
        }

        // Resolve relative paths from git diff against the git working directory (PWD from CD)
        string basePath = PWD;

        var changed = new List<string>();
        foreach (string project in projects) {
            string projectDir = Path.GetFullPath(Path.GetDirectoryName(project)!);
            bool hasChange = changedFiles.Any(f => {
                string fullPath = Path.GetFullPath(Path.Combine(basePath, f));
                return fullPath.StartsWith(projectDir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
            });

            if (hasChange) {
                changed.Add(project);
            }
        }

        return changed;
    }

    private static List<string> GetChangedFilesSinceLastTag(string remote, string tagPrefix) {
        // Find the latest local tag matching the prefix
        string lastTag;
        try {
            lastTag = CMD($"git describe --tags --match \"{tagPrefix}[0-9]*.[0-9]*.[0-9]*\" --abbrev=0")
                .Trim();
        } catch {
            // No tags found — all committed files are "changed" (diff against empty tree)
            try {
                string output = CMD("git ls-files");
                return ParseFileList(output);
            } catch {
                return [];
            }
        }

        try {
            string output = CMD($"git diff --name-only {lastTag}..HEAD");
            return ParseFileList(output);
        } catch {
            return [];
        }
    }

    private static List<string> ParseFileList(string output) =>
        [.. output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];
}
