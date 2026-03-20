namespace CalqFramework.Flow.Discovery;

/// <summary>
///     Finds associated test projects by searching upward from a library project's directory (§5).
/// </summary>
public static class TestProjectAssociation {
    /// <summary>
    ///     Returns a mapping of library project path → test project path.
    ///     Searches upward from each project's directory for files matching {ProjectName}Test*.*proj.
    ///     Traversal is bounded by the Git repository root.
    /// </summary>
    public static Dictionary<string, string> FindTestProjects(List<string> projects, string repoRoot) {
        var result = new Dictionary<string, string>();
        string normalizedRepoRoot = Path.GetFullPath(repoRoot);

        foreach (string project in projects) {
            string projectName = Path.GetFileNameWithoutExtension(project);
            string searchPattern = $"{projectName}Test*.*proj";
            string startDir = Path.GetDirectoryName(project)!;

            string? testProject = SearchUpward(startDir, searchPattern, normalizedRepoRoot);
            if (testProject != null) {
                result[project] = testProject;
            }
        }

        return result;
    }

    private static string? SearchUpward(string startDir, string searchPattern, string repoRoot) {
        string currentDir = Path.GetFullPath(Path.Combine(startDir, ".."));

        while (IsWithinBoundary(currentDir, repoRoot)) {
            string[] matches = Directory.GetFiles(currentDir, searchPattern, SearchOption.AllDirectories);
            if (matches.Length > 0) {
                return Path.GetFullPath(matches[0]);
            }

            string? parent = Path.GetDirectoryName(currentDir);
            if (parent == null || parent == currentDir) {
                break;
            }

            currentDir = parent;
        }

        return null;
    }

    private static bool IsWithinBoundary(string dir, string repoRoot) {
        string normalizedDir = Path.GetFullPath(dir);
        return normalizedDir.StartsWith(repoRoot, StringComparison.OrdinalIgnoreCase);
    }
}
