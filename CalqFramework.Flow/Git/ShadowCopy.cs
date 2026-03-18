using static CalqFramework.Cmd.Terminal;

namespace CalqFramework.Flow.Git;

/// <summary>
/// Creates an isolated shadow copy of the working directory for safe builds (§13).
/// </summary>
public static class ShadowCopy {
    private static readonly HashSet<string> ExcludedDirs = new(StringComparer.OrdinalIgnoreCase) {
        "bin", "obj", ".vs"
    };

    /// <summary>
    /// Creates a physical copy of the working directory into a temp path,
    /// excluding bin/obj/.vs folders. Then fetches and checks out the base version.
    /// </summary>
    public static string Create(string workingDirectory, string repoRoot, string remote, string tagPrefix) {
        var shadowPath = Path.Combine(Path.GetTempPath(), $"flow-shadow-{Guid.NewGuid():N}");
        CopyDirectory(workingDirectory, shadowPath);

        // §13: Context Acquisition — fetch the base commit
        var commitHash = GitOperations.GetHeadCommitHash();
        var lastTag = GetLastTag(tagPrefix);

        if (lastTag != null) {
            var baseCommit = CMD($"git rev-list -n 1 {lastTag}").Trim();

            // CD into shadow copy — AsyncLocal, thread-safe
            var previousDir = PWD;
            CD(shadowPath);
            try {
                // §13: Context Acquisition
                RUN($"git fetch {remote} {baseCommit} --depth 1");

                // §13: Sanitization — only inside shadow copy
                RUN("git reset --hard");
                RUN("git clean -d -x --force");

                // §13: State Switch — checkout the base version
                RUN($"git checkout {baseCommit}");
            } finally {
                CD(previousDir);
            }
        }

        return shadowPath;
    }

    /// <summary>
    /// Recursively deletes the shadow copy directory.
    /// </summary>
    public static void Cleanup(string shadowPath) {
        if (Directory.Exists(shadowPath)) {
            Directory.Delete(shadowPath, true);
        }
    }

    private static string? GetLastTag(string tagPrefix) {
        try {
            return CMD($"git describe --tags --match \"{tagPrefix}[0-9]*.[0-9]*.[0-9]*\" --abbrev=0").Trim();
        } catch {
            return null;
        }
    }

    private static void CopyDirectory(string source, string destination) {
        Directory.CreateDirectory(destination);

        foreach (var file in Directory.GetFiles(source)) {
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)));
        }

        foreach (var dir in Directory.GetDirectories(source)) {
            var dirName = Path.GetFileName(dir);
            if (ExcludedDirs.Contains(dirName)) continue;
            CopyDirectory(dir, Path.Combine(destination, dirName));
        }
    }
}
