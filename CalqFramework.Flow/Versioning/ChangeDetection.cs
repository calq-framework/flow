using static CalqFramework.Cmd.Terminal;

namespace CalqFramework.Flow.Versioning;

/// <summary>
/// Detects which projects have changed since the last version tag (§7).
/// A project is "changed" only if a file modification occurs within
/// the project's own directory or its subdirectories.
/// </summary>
public static class ChangeDetection {
    /// <summary>
    /// Returns the subset of projects that have file changes since the last tag.
    /// </summary>
    public static List<string> DetectChangedProjects(
        List<string> projects, string remote, string tagPrefix) {
        var changedFiles = GetChangedFilesSinceLastTag(remote, tagPrefix);
        if (changedFiles.Count == 0) return new List<string>();

        // Resolve relative paths from git diff against the git working directory (PWD from CD)
        var basePath = PWD;

        var changed = new List<string>();
        foreach (var project in projects) {
            var projectDir = Path.GetFullPath(Path.GetDirectoryName(project)!);
            var hasChange = changedFiles.Any(f => {
                var fullPath = Path.GetFullPath(Path.Combine(basePath, f));
                return fullPath.StartsWith(
                    projectDir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
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
            lastTag = CMD($"git describe --tags --match \"{tagPrefix}[0-9]*.[0-9]*.[0-9]*\" --abbrev=0").Trim();
        } catch {
            // No tags found — everything is changed relative to the initial commit
            try {
                var output = CMD("git diff --name-only HEAD");
                return ParseFileList(output);
            } catch {
                return new List<string>();
            }
        }

        try {
            var output = CMD($"git diff --name-only {lastTag}..HEAD");
            return ParseFileList(output);
        } catch {
            return new List<string>();
        }
    }

    private static List<string> ParseFileList(string output) {
        return output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();
    }
}
