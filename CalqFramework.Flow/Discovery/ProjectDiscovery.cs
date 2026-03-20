namespace CalqFramework.Flow.Discovery;

/// <summary>
///     Recursively scans for project files with exclusion logic (§4).
/// </summary>
public static class ProjectDiscovery {
    private static readonly string[] ExclusionPatterns = [
        "*Test.*proj",
        "*Tests.*proj",
        "*Example.*proj",
        "*Examples.*proj",
        "*Sample.*proj",
        "*Samples.*proj"
    ];

    /// <summary>
    ///     Discovers all non-test, non-sample, non-nested project files.
    /// </summary>
    public static List<string> DiscoverProjects(string workingDirectory) {
        var allProjects = Directory.GetFiles(workingDirectory, "*.*proj", SearchOption.AllDirectories)
            .Where(p => !IsExcluded(p))
            .Select(Path.GetFullPath)
            .OrderBy(p => p.Length)
            .ToList();

        return RemoveNestedProjects(allProjects);
    }

    private static bool IsExcluded(string projectPath) {
        string fileName = Path.GetFileName(projectPath);
        foreach (string pattern in ExclusionPatterns) {
            if (MatchesWildcard(fileName, pattern)) {
                return true;
            }
        }

        // Exclude if the project resides in an identically named test/sample directory
        string dirName = Path.GetFileName(Path.GetDirectoryName(projectPath) ?? "");
        foreach (string pattern in ExclusionPatterns) {
            string dirPattern = pattern.Replace(".*proj", "");
            if (MatchesWildcard(dirName, dirPattern)) {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    ///     If a project file resides in the same directory or a subdirectory
    ///     of another discovered project file, the nested one is ignored.
    /// </summary>
    private static List<string> RemoveNestedProjects(List<string> projects) {
        var result = new List<string>();
        var projectDirs = new List<string>();

        foreach (string project in projects) {
            string dir = Path.GetDirectoryName(project)!;
            bool isNested = projectDirs.Any(parentDir => dir.StartsWith(parentDir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || dir.Equals(parentDir, StringComparison.OrdinalIgnoreCase));

            if (!isNested) {
                // Check if this project's dir already has a project (same directory case)
                string? sameDir = result.FirstOrDefault(p => Path.GetDirectoryName(p)!.Equals(dir, StringComparison.OrdinalIgnoreCase));
                if (sameDir != null) {
                    continue;
                }

                result.Add(project);
                projectDirs.Add(dir);
            }
        }

        return result;
    }

    private static bool MatchesWildcard(string input, string pattern) {
        string regexPattern = "^" + System.Text.RegularExpressions.Regex.Escape(pattern)
            .Replace("\\*", ".*") + "$";
        return System.Text.RegularExpressions.Regex.IsMatch(input, regexPattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    }
}
