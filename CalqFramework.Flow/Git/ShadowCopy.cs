namespace CalqFramework.Flow.Git;

/// <summary>
///     Creates an isolated shadow copy of the working directory for safe builds (§13).
/// </summary>
public static class ShadowCopy {
    private static readonly HashSet<string> ExcludedDirs = new(StringComparer.OrdinalIgnoreCase) {
        "bin",
        "obj",
        ".vs"
    };

    /// <summary>
    ///     Creates a physical copy of the working directory into a temp path,
    ///     excluding bin/obj/.vs folders. Then fetches and checks out the base version.
    /// </summary>
    public static string Create(string workingDirectory, string repoRoot, string remote, string tagPrefix) {
        string shadowPath = Path.Combine(Path.GetTempPath(), $"flow-shadow-{Guid.NewGuid():N}");
        CopyDirectory(workingDirectory, shadowPath);

        // §13: Context Acquisition — fetch the base commit
        string commitHash = GitOperations.GetHeadCommitHash();
        string? lastTag = GetLastTag(tagPrefix);

        if (lastTag != null) {
            string baseCommit = CMD($"git rev-list -n 1 {lastTag}")
                .Trim();

            // CD into shadow copy — AsyncLocal, thread-safe
            string previousDir = PWD;
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
    ///     Recursively deletes the shadow copy directory.
    /// </summary>
    public static void Cleanup(string shadowPath) {
        if (Directory.Exists(shadowPath)) {
            // Reset readonly attributes (git pack files on Windows)
            foreach (string file in Directory.GetFiles(shadowPath, "*", SearchOption.AllDirectories)) {
                File.SetAttributes(file, FileAttributes.Normal);
            }

            Directory.Delete(shadowPath, true);
        }
    }

    private static string? GetLastTag(string tagPrefix) {
        try {
            return CMD($"git describe --tags --match \"{tagPrefix}[0-9]*.[0-9]*.[0-9]*\" --abbrev=0")
                .Trim();
        } catch {
            return null;
        }
    }

    private static void CopyDirectory(string source, string destination) {
        Directory.CreateDirectory(destination);

        foreach (string file in Directory.GetFiles(source)) {
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)));
        }

        foreach (string dir in Directory.GetDirectories(source)) {
            string dirName = Path.GetFileName(dir);
            if (ExcludedDirs.Contains(dirName)) {
                continue;
            }

            CopyDirectory(dir, Path.Combine(destination, dirName));
        }
    }
}
